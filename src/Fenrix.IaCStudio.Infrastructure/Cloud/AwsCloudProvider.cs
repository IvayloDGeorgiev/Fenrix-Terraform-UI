using System.Text.Json;
using Fenrix.IaCStudio.Application.Abstractions.Cloud;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Contracts.Cloud;
using Fenrix.IaCStudio.Contracts.Providers;
using Fenrix.IaCStudio.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Cloud;

/// <summary>
/// AWS cloud adapter. Fenrix references a named profile / IAM Identity Center (SSO) session rather than
/// copying keys into its database (docs/10-cloud-integrations.md): it composes <c>AWS_PROFILE</c> and
/// <c>AWS_REGION</c>/<c>AWS_DEFAULT_REGION</c>, and the AWS SDK inside Terraform resolves the actual
/// credentials from the shared config / SSO cache at execution time. The test calls
/// <c>aws sts get-caller-identity</c> with that same environment.
/// </summary>
public sealed class AwsCloudProvider(IProcessRunner runner, ILogger<AwsCloudProvider> logger) : ICloudConnectionProvider
{
    private const string Tool = "aws";
    private readonly IProcessRunner _runner = runner;
    private readonly ILogger<AwsCloudProvider> _logger = logger;

    public CloudProviderType ProviderType => CloudProviderType.Aws;

    public Task<IReadOnlyDictionary<string, string>> BuildEnvironmentAsync(
        CloudConnectionContext context, CancellationToken ct = default)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(context.ProfileName))
            env["AWS_PROFILE"] = context.ProfileName!.Trim();
        if (!string.IsNullOrWhiteSpace(context.Region))
        {
            env["AWS_REGION"] = context.Region!.Trim();
            env["AWS_DEFAULT_REGION"] = context.Region!.Trim();
        }
        return Task.FromResult<IReadOnlyDictionary<string, string>>(env);
    }

    public async Task<ProviderResult<CloudIdentity>> TestAsync(
        CloudConnectionContext context, CancellationToken ct = default)
    {
        var env = await BuildEnvironmentAsync(context, ct);
        var run = await CloudCli.RunAsync(_runner, Tool, ["sts", "get-caller-identity", "--output", "json"], env, ct);
        if (!run.Started)
            return ProviderResult<CloudIdentity>.Fail(ProviderErrorKind.Network,
                "The AWS CLI (aws) was not found on PATH. Install it and configure a profile / SSO.");
        if (run.ExitCode != 0)
            return ProviderResult<CloudIdentity>.Fail(ProviderErrorKind.Authentication,
                Trim(run.StdErr) ??
                $"AWS credentials are missing or expired. For IAM Identity Center run 'aws sso login --profile {context.ProfileName ?? "<profile>"}'.");

        try
        {
            using var doc = JsonDocument.Parse(run.StdOut);
            var root = doc.RootElement;
            var account = GetString(root, "Account");
            var arn = GetString(root, "Arn");
            return ProviderResult<CloudIdentity>.Ok(new CloudIdentity(
                account ?? "AWS", arn,
                context.ProfileName is { } p ? $"profile {p}" : null));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse 'aws sts get-caller-identity' output.");
            return ProviderResult<CloudIdentity>.Ok(new CloudIdentity("AWS", null, "authenticated"));
        }
    }

    public async Task<ProviderResult<IReadOnlyList<CloudScope>>> GetAvailableScopesAsync(
        CloudConnectionContext context, CancellationToken ct = default)
    {
        var run = await CloudCli.RunAsync(_runner, Tool, ["configure", "list-profiles"], env: null, ct);
        if (!run.Started)
            return ProviderResult<IReadOnlyList<CloudScope>>.Fail(ProviderErrorKind.Network,
                "The AWS CLI (aws) was not found on PATH.");
        if (run.ExitCode != 0)
            return ProviderResult<IReadOnlyList<CloudScope>>.Fail(ProviderErrorKind.Unknown,
                Trim(run.StdErr) ?? "Could not list AWS profiles.");

        var scopes = run.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => new CloudScope(p, p, "AWS profile", p.Equals("default", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return ProviderResult<IReadOnlyList<CloudScope>>.Ok(scopes);
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
