using System.Text.Json;
using Fenrix.IaCStudio.Application.Abstractions.Cloud;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Contracts.Cloud;
using Fenrix.IaCStudio.Contracts.Providers;
using Fenrix.IaCStudio.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Cloud;

/// <summary>
/// Azure cloud adapter. Two auth paths, both composed at execution time and never stored as a value by
/// Fenrix (docs/10-cloud-integrations.md):
/// <list type="bullet">
///   <item><description><b>Azure CLI login</b> — the connection names a tenant/subscription and Terraform's azurerm
///   provider reuses the <c>az</c> CLI session. Fenrix only sets <c>ARM_TENANT_ID</c>/<c>ARM_SUBSCRIPTION_ID</c>.</description></item>
///   <item><description><b>Service principal</b> — when a client id and a stored client secret are present, Fenrix composes
///   <c>ARM_CLIENT_ID</c>/<c>ARM_CLIENT_SECRET</c>/<c>ARM_TENANT_ID</c>/<c>ARM_SUBSCRIPTION_ID</c>. The secret is
///   resolved just-in-time and passed only to the child process.</description></item>
/// </list>
/// </summary>
public sealed class AzureCloudProvider(IProcessRunner runner, ILogger<AzureCloudProvider> logger) : ICloudConnectionProvider
{
    private const string Tool = "az";
    private readonly IProcessRunner _runner = runner;
    private readonly ILogger<AzureCloudProvider> _logger = logger;

    public CloudProviderType ProviderType => CloudProviderType.Azure;

    public Task<IReadOnlyDictionary<string, string>> BuildEnvironmentAsync(
        CloudConnectionContext context, CancellationToken ct = default)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(context.TenantOrAccountId))
            env["ARM_TENANT_ID"] = context.TenantOrAccountId!.Trim();
        if (!string.IsNullOrWhiteSpace(context.SubscriptionOrProjectId))
            env["ARM_SUBSCRIPTION_ID"] = context.SubscriptionOrProjectId!.Trim();

        // Service-principal auth only when we have both the client id and its secret.
        if (context.HasSecret && !string.IsNullOrWhiteSpace(context.ServicePrincipalClientId))
        {
            env["ARM_CLIENT_ID"] = context.ServicePrincipalClientId!.Trim();
            env["ARM_CLIENT_SECRET"] = context.Secret!;
        }

        return Task.FromResult<IReadOnlyDictionary<string, string>>(env);
    }

    public async Task<ProviderResult<CloudIdentity>> TestAsync(
        CloudConnectionContext context, CancellationToken ct = default)
    {
        // Service principal: we have credentials but verifying them via `az` would mutate the CLI login
        // state (az login/logout). Report the stored identity without a live sign-in side effect.
        if (context.HasSecret && !string.IsNullOrWhiteSpace(context.ServicePrincipalClientId))
        {
            return ProviderResult<CloudIdentity>.Ok(new CloudIdentity(
                $"sp:{context.ServicePrincipalClientId}",
                "Service principal",
                context.SubscriptionOrProjectId is { } s ? $"subscription {s}" : "credentials stored (not live-tested)"));
        }

        var args = new List<string> { "account", "show", "-o", "json" };
        if (!string.IsNullOrWhiteSpace(context.SubscriptionOrProjectId))
        {
            args.Add("--subscription");
            args.Add(context.SubscriptionOrProjectId!.Trim());
        }

        var run = await CloudCli.RunAsync(_runner, Tool, args, env: null, ct);
        if (!run.Started)
            return ProviderResult<CloudIdentity>.Fail(ProviderErrorKind.Network,
                "The Azure CLI (az) was not found on PATH. Install it and run 'az login', or use service-principal auth on this connection.");
        if (run.ExitCode != 0)
            return ProviderResult<CloudIdentity>.Fail(ProviderErrorKind.Authentication,
                Trim(run.StdErr) ?? "Not signed in to the Azure CLI. Run 'az login' (and 'az account set --subscription <id>').");

        try
        {
            using var doc = JsonDocument.Parse(run.StdOut);
            var root = doc.RootElement;
            var subId = GetString(root, "id");
            var subName = GetString(root, "name");
            var tenant = GetString(root, "tenantId");
            var user = root.TryGetProperty("user", out var u) ? GetString(u, "name") : null;
            return ProviderResult<CloudIdentity>.Ok(new CloudIdentity(
                user ?? subId ?? "Azure", subName, tenant is null ? null : $"tenant {tenant}"));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse 'az account show' output.");
            return ProviderResult<CloudIdentity>.Ok(new CloudIdentity("Azure", null, "signed in"));
        }
    }

    public async Task<ProviderResult<IReadOnlyList<CloudScope>>> GetAvailableScopesAsync(
        CloudConnectionContext context, CancellationToken ct = default)
    {
        var run = await CloudCli.RunAsync(_runner, Tool, ["account", "list", "--all", "-o", "json"], env: null, ct);
        if (!run.Started)
            return ProviderResult<IReadOnlyList<CloudScope>>.Fail(ProviderErrorKind.Network,
                "The Azure CLI (az) was not found on PATH.");
        if (run.ExitCode != 0)
            return ProviderResult<IReadOnlyList<CloudScope>>.Fail(ProviderErrorKind.Authentication,
                Trim(run.StdErr) ?? "Not signed in to the Azure CLI. Run 'az login'.");

        var scopes = new List<CloudScope>();
        try
        {
            using var doc = JsonDocument.Parse(run.StdOut);
            foreach (var sub in doc.RootElement.EnumerateArray())
            {
                var id = GetString(sub, "id");
                if (id is null) continue;
                var name = GetString(sub, "name") ?? id;
                var tenant = GetString(sub, "tenantId");
                var isDefault = sub.TryGetProperty("isDefault", out var d) && d.ValueKind == JsonValueKind.True;
                scopes.Add(new CloudScope(id, name, tenant is null ? null : $"tenant {tenant}", isDefault));
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse 'az account list' output.");
        }
        return ProviderResult<IReadOnlyList<CloudScope>>.Ok(scopes);
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
