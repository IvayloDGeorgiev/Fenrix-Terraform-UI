using System.Text.Json;
using System.Text.Json.Serialization;
using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Application.Abstractions.Enterprise;
using Fenrix.IaCStudio.Contracts.Enterprise;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Enterprise;

/// <summary>
/// Reads the bootstrap <c>enterprise.json</c> from the data root once at startup and resolves whether
/// enterprise governance is enabled and which metadata provider (SQLite / SQL Server) is active. The
/// SQL Server connection string is read from the environment variable the file names — never stored on
/// disk (see docs/29-enterprise.md, ADR-0006). The provider must be known before any row is read, so this
/// runs at DI time, ahead of <c>AddDbContext</c>. Absent/disabled/unresolved ⇒ local SQLite.
/// </summary>
public sealed class EnterpriseBootstrap : IEnterpriseConfig
{
    public const string FileName = "enterprise.json";

    private EnterpriseBootstrap(
        bool enabled, bool useSqlServer, string? sqlConnectionString, string? organisation)
    {
        IsEnabled = enabled;
        UseSqlServer = useSqlServer;
        SqlConnectionString = sqlConnectionString;
        Organisation = organisation;
    }

    /// <summary>True when governance is active. Requires a resolved SQL Server connection when the file asks for it.</summary>
    public bool IsEnabled { get; }

    public string MetadataProvider => UseSqlServer ? "SqlServer" : "Sqlite";
    public string? Organisation { get; }

    /// <summary>Infrastructure-only: whether to wire <c>UseSqlServer</c>. Not on the interface.</summary>
    public bool UseSqlServer { get; }

    /// <summary>Infrastructure-only: the resolved connection string. Never exposed on <see cref="IEnterpriseConfig"/>.</summary>
    public string? SqlConnectionString { get; }

    public EnterpriseStatus Status => new(
        IsEnabled, MetadataProvider, Organisation,
        ConnectionResolved: !UseSqlServer || !string.IsNullOrWhiteSpace(SqlConnectionString));

    /// <summary>Loads the bootstrap config, degrading safely to local SQLite on any problem.</summary>
    public static EnterpriseBootstrap Load(IWorkspacePaths paths, ILogger? logger = null)
    {
        var path = Path.Combine(paths.DataRoot, FileName);
        if (!File.Exists(path))
            return LocalDefault();

        try
        {
            var file = JsonSerializer.Deserialize<EnterpriseFile>(
                File.ReadAllText(path), SerializerOptions) ?? new EnterpriseFile();

            if (!file.Enabled)
                return LocalDefault(file.Organisation);

            var wantSqlServer = string.Equals(
                file.MetadataProvider, "SqlServer", StringComparison.OrdinalIgnoreCase);

            string? connectionString = null;
            if (wantSqlServer)
            {
                if (!string.IsNullOrWhiteSpace(file.ConnectionStringEnvVar))
                    connectionString = Environment.GetEnvironmentVariable(file.ConnectionStringEnvVar!);

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    logger?.LogWarning(
                        "Enterprise mode requested SQL Server but the connection string env var '{Var}' is unset — falling back to local SQLite.",
                        file.ConnectionStringEnvVar);
                    // Governance stays enabled, but the store degrades to local SQLite (better than failing to start).
                    return new EnterpriseBootstrap(enabled: true, useSqlServer: false, null, file.Organisation);
                }
            }

            return new EnterpriseBootstrap(
                enabled: true, useSqlServer: wantSqlServer, connectionString, file.Organisation);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to read {File} — falling back to local SQLite (enterprise mode off).", path);
            return LocalDefault();
        }
    }

    private static EnterpriseBootstrap LocalDefault(string? organisation = null)
        => new(enabled: false, useSqlServer: false, null, organisation);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private sealed class EnterpriseFile
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("metadataProvider")] public string? MetadataProvider { get; set; }
        [JsonPropertyName("connectionStringEnvVar")] public string? ConnectionStringEnvVar { get; set; }
        [JsonPropertyName("organisation")] public string? Organisation { get; set; }
    }
}
