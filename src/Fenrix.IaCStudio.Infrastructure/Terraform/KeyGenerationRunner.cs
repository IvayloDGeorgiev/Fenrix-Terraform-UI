using System.Text.Json;
using Fenrix.IaCStudio.Application.Abstractions.Cloud;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Application.Security;
using Fenrix.IaCStudio.Application.Terraform;
using Fenrix.IaCStudio.Contracts.Cloud;
using Fenrix.IaCStudio.Contracts.Terraform;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// Runs the self-contained key-pair generation flow in a working directory: <c>init</c> → <c>apply
/// -auto-approve</c> → <c>output -json</c>, all through the shared <see cref="TerraformProcessCoordinator"/> so
/// each invocation is previewed identically to execution and recorded as redacted history. The generated
/// private key is captured from the JSON output <em>in memory</em> (parsed directly, never via the redacting
/// output parser, and never logged — <c>captureLog:false</c>) and handed back to the key service to encrypt
/// and store. See docs/28-key-pair-management.md, docs/06-plan-apply-safety.md, docs/11-secrets.md.
/// </summary>
public sealed class KeyGenerationRunner(
    ITerraformDiscovery discovery,
    TerraformProcessCoordinator coordinator,
    ICloudEnvironmentComposer cloud,
    ILogger<KeyGenerationRunner> logger)
{
    private const string DefaultExecutable = "terraform";

    private readonly ITerraformDiscovery _discovery = discovery;
    private readonly TerraformProcessCoordinator _coordinator = coordinator;
    private readonly ICloudEnvironmentComposer _cloud = cloud;
    private readonly ILogger<KeyGenerationRunner> _logger = logger;

    public sealed record GenerationResult(
        bool Succeeded,
        string? Error,
        Guid? RunId,
        string? PrivateKeyPem,
        string? PublicKeyOpenSsh,
        string? Fingerprint,
        string? CloudKeyName)
    {
        public static GenerationResult Fail(string error, Guid? runId = null) =>
            new(false, error, runId, null, null, null, null);
    }

    public async Task<GenerationResult> RunAsync(
        Guid projectId,
        string workingDirectory,
        string configHcl,
        Guid? cloudConnectionId,
        IProgress<ProcessOutputEvent>? output,
        CancellationToken ct = default)
    {
        var installation = await _discovery.ResolveAsync(projectId, ct);
        if (installation is null)
            return GenerationResult.Fail("No Terraform binary found. Set the executable in Settings or install Terraform on your PATH.");
        var exe = installation.ExecutablePath;

        Directory.CreateDirectory(workingDirectory);
        await File.WriteAllTextAsync(Path.Combine(workingDirectory, "main.tf"), configHcl, ct);

        var cloudEnv = cloudConnectionId is null
            ? CloudEnvironmentResult.None
            : await _cloud.ComposeAsync(cloudConnectionId, ct);

        if (cloudConnectionId is not null && !cloudEnv.HasConnection)
            return GenerationResult.Fail("The selected environment has no usable cloud connection to register the key against.");

        // 1) init (downloads the tls/aws providers; safe to log).
        var initRun = await RunAsync(TerraformCommandKind.Init, projectId, exe, workingDirectory, cloudEnv, output, captureLog: true, ct);
        if (!initRun.Process.Succeeded)
            return GenerationResult.Fail($"terraform init failed (exit {initRun.Process.ExitCode}).", initRun.RunId);

        // 2) apply -auto-approve (output can echo sensitive attributes → never logged).
        var applyRun = await RunAsync(TerraformCommandKind.KeyPairGenerateApply, projectId, exe, workingDirectory, cloudEnv, output, captureLog: false, ct);
        if (applyRun.Process.Cancelled)
            return GenerationResult.Fail("Key generation was cancelled.", applyRun.RunId);
        if (!applyRun.Process.Succeeded)
            return GenerationResult.Fail($"Key generation apply failed (exit {applyRun.Process.ExitCode}).", applyRun.RunId);

        // 3) output -json — read the sensitive private key in memory (never logged, never via the redactor).
        var outRun = await RunAsync(TerraformCommandKind.Output, projectId, exe, workingDirectory, cloudEnv, output: null, captureLog: false, ct);
        var outputs = ParseOutputs(outRun.StandardOutput);

        outputs.TryGetValue(KeyGeneratorConfig.PrivateKeyOutput, out var privateKey);
        outputs.TryGetValue(KeyGeneratorConfig.PublicKeyOutput, out var publicKey);
        outputs.TryGetValue(KeyGeneratorConfig.FingerprintOutput, out var fingerprint);
        outputs.TryGetValue(KeyGeneratorConfig.CloudKeyNameOutput, out var cloudKeyName);

        if (string.IsNullOrWhiteSpace(privateKey))
            return GenerationResult.Fail("Generation apply succeeded but the private key output could not be read.", applyRun.RunId);

        return new GenerationResult(true, null, applyRun.RunId, privateKey, publicKey, NormalizeFingerprint(fingerprint), cloudKeyName);
    }

    /// <summary>
    /// De-registers a cloud-registered generated key by running <c>destroy -auto-approve</c> in its kept
    /// working directory. Returns true on success (or when the dir no longer exists). See docs/28-key-pair-management.md.
    /// </summary>
    public async Task<bool> DestroyAsync(
        Guid projectId, string workingDirectory, Guid? cloudConnectionId,
        IProgress<ProcessOutputEvent>? output, CancellationToken ct = default)
    {
        if (!Directory.Exists(workingDirectory))
            return true;

        var installation = await _discovery.ResolveAsync(projectId, ct);
        if (installation is null)
        {
            _logger.LogWarning("Cannot de-register key: no Terraform binary found.");
            return false;
        }

        var cloudEnv = cloudConnectionId is null
            ? CloudEnvironmentResult.None
            : await _cloud.ComposeAsync(cloudConnectionId, ct);

        var run = await RunAsync(TerraformCommandKind.KeyPairGenerateDestroy, projectId, installation.ExecutablePath, workingDirectory, cloudEnv, output, captureLog: false, ct);
        return run.Process.Succeeded;
    }

    private Task<TerraformProcessCoordinator.CoordinatedRun> RunAsync(
        TerraformCommandKind kind, Guid projectId, string exe, string workingDir,
        CloudEnvironmentResult cloudEnv, IProgress<ProcessOutputEvent>? output, bool captureLog, CancellationToken ct)
    {
        var spec = new TerraformRunSpec(projectId, Guid.Empty, kind);
        var request = CommandPreviewBuilder.BuildRequest(spec, exe, workingDir, cloudEnv.EnvironmentVariables);
        return _coordinator.RunAsync(request, output, captureLog, ct);
    }

    /// <summary>Parses <c>terraform output -json</c> ( <c>{ name: { value, sensitive, type } }</c> ) to name→value.</summary>
    private static Dictionary<string, string> ParseOutputs(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
            return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return result;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object &&
                    prop.Value.TryGetProperty("value", out var v) &&
                    v.ValueKind == JsonValueKind.String)
                {
                    result[prop.Name] = v.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
            // Malformed/empty output — caller treats the missing private key as a failure.
        }
        return result;
    }

    /// <summary>The tls provider emits a bare hex/base64 fingerprint; prefix it to the OpenSSH SHA256 form.</summary>
    private static string? NormalizeFingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.StartsWith("SHA256:", StringComparison.Ordinal) ? value : "SHA256:" + value;
    }
}
