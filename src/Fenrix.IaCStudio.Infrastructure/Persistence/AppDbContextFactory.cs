using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fenrix.IaCStudio.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can build the model and generate migrations against the
/// Infrastructure project directly (no MAUI head required). Not used at runtime.
///
/// <para>Provider is chosen by the <c>FENRIX_DESIGNTIME_PROVIDER</c> environment variable so both
/// migration sets can be generated from one factory (see docs/29-enterprise.md):</para>
/// <list type="bullet">
///   <item><description>unset / <c>Sqlite</c> → SQLite migrations under <c>Migrations/</c>:
///     <code>dotnet ef migrations add AddEnterpriseCapability -p src/Fenrix.IaCStudio.Infrastructure -s src/Fenrix.IaCStudio.Infrastructure</code></description></item>
///   <item><description><c>SqlServer</c> → SQL Server migrations under <c>Migrations/SqlServer/</c>:
///     <code>set FENRIX_DESIGNTIME_PROVIDER=SqlServer
/// dotnet ef migrations add AddEnterpriseCapability_Sql -p src/Fenrix.IaCStudio.Infrastructure -s src/Fenrix.IaCStudio.Infrastructure -o Migrations/SqlServer</code></description></item>
/// </list>
/// The design-time SQL Server connection string is a throwaway; the real one is resolved at runtime from
/// the <c>enterprise.json</c> bootstrap.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("FENRIX_DESIGNTIME_PROVIDER");
        var builder = new DbContextOptionsBuilder<AppDbContext>();

        if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            // A throwaway design-time connection string; only the model shape matters for migrations.
            var cs = Environment.GetEnvironmentVariable("FENRIX_DESIGNTIME_SQL")
                     ?? "Server=(localdb)\\MSSQLLocalDB;Database=FenrixDesignTime;Trusted_Connection=True;TrustServerCertificate=True";
            builder.UseSqlServer(cs, o => o.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
        }
        else
        {
            var dbPath = Path.Combine(Path.GetTempPath(), "fenrix-designtime.db");
            builder.UseSqlite($"Data Source={dbPath}");
        }

        return new AppDbContext(builder.Options);
    }
}
