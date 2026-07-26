using System.Text.Json;
using Fenrix.IaCStudio.Application.Abstractions.Cloud;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Contracts.Cloud;
using Fenrix.IaCStudio.Contracts.Providers;
using Fenrix.IaCStudio.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Cloud;

/// <summary>
/// Google Cloud adapter. Uses Application Default Credentials (ADC) established by
/// <c>gcloud auth application-default login</c>, plus a selected project — composing
/// <c>GOOGLE_PROJECT</c>/<c>GOOGLE_CLOUD_PROJECT</c> and, when the connection points at a service-account
/// file, <c>GOOGLE_APPLICATION_CREDENTIALS</c> (a file <em>path</em>, never the JSON contents — docs/10). The
/// test reads the active gcloud account.
/// </summary>
public sealed class GoogleCloudProvider(IProcessRunner runner, ILogger<GoogleCloudProvider> logger) : ICloudConnectionProvider
{
    /// <summary>Metadata key holding the service-account key file path (not its contents).</summary>
    public const string ServiceAccountFileKey = "serviceAccountFile";

    private const string Tool = "gcloud";
    private readonly IProcessRunner _runner = runner;
    private readonly ILogger<GoogleCloudProvider> _logger = logger;

    public CloudProviderType ProviderType => CloudProviderType.GoogleCloud;

    public Task<IReadOnlyDictionary<string, string>> BuildEnvironmentAsync(
        CloudConnectionContext context, CancellationToken ct = default)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(context.SubscriptionOrProjectId))
        {
            env["GOOGLE_PROJECT"] = context.SubscriptionOrProjectId!.Trim();
            env["GOOGLE_CLOUD_PROJECT"] = context.SubscriptionOrProjectId!.Trim();
        }
        if (context.MetadataValue(ServiceAccountFileKey) is { } saFile)
            env["GOOGLE_APPLICATION_CREDENTIALS"] = saFile;
        return Task.FromResult<IReadOnlyDictionary<string, string>>(env);
    }

    public async Task<ProviderResult<CloudIdentity>> TestAsync(
        CloudConnectionContext context, CancellationToken ct = default)
    {
        var run = await CloudCli.RunAsync(
            _runner, Tool, ["auth", "list", "--filter=status:ACTIVE", "--format=value(account)"], env: null, ct);
        if (!run.Started)
            return ProviderResult<CloudIdentity>.Fail(ProviderErrorKind.Network,
                "The Google Cloud CLI (gcloud) was not found on PATH. Install it and run 'gcloud auth application-default login'.");
        if (run.ExitCode != 0)
            return ProviderResult<CloudIdentity>.Fail(ProviderErrorKind.Authentication,
                Trim(run.StdErr) ?? "No active gcloud credentials. Run 'gcloud auth application-default login'.");

        var account = Trim(run.StdOut);
        if (account is null)
            return ProviderResult<CloudIdentity>.Fail(ProviderErrorKind.Authentication,
                "No active gcloud account. Run 'gcloud auth login' and 'gcloud auth application-default login'.");

        return ProviderResult<CloudIdentity>.Ok(new CloudIdentity(
            account.Split('\n')[0].Trim(), null,
            context.SubscriptionOrProjectId is { } p ? $"project {p}" : null));
    }

    public async Task<ProviderResult<IReadOnlyList<CloudScope>>> GetAvailableScopesAsync(
        CloudConnectionContext context, CancellationToken ct = default)
    {
        var run = await CloudCli.RunAsync(_runner, Tool, ["projects", "list", "--format=json"], env: null, ct);
        if (!run.Started)
            return ProviderResult<IReadOnlyList<CloudScope>>.Fail(ProviderErrorKind.Network,
                "The Google Cloud CLI (gcloud) was not found on PATH.");
        if (run.ExitCode != 0)
            return ProviderResult<IReadOnlyList<CloudScope>>.Fail(ProviderErrorKind.Authentication,
                Trim(run.StdErr) ?? "Could not list projects. Run 'gcloud auth login'.");

        var scopes = new List<CloudScope>();
        try
        {
            using var doc = JsonDocument.Parse(run.StdOut);
            foreach (var proj in doc.RootElement.EnumerateArray())
            {
                var id = GetString(proj, "projectId");
                if (id is null) continue;
                var name = GetString(proj, "name") ?? id;
                scopes.Add(new CloudScope(id, name, "GCP project"));
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse 'gcloud projects list' output.");
        }
        return ProviderResult<IReadOnlyList<CloudScope>>.Ok(scopes);
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
