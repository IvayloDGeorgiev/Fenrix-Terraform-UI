using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fenrix.IaCStudio.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can build the model and generate migrations against the
/// Infrastructure project directly (no MAUI head required). Not used at runtime.
/// Generate the initial migration with:
/// <code>dotnet ef migrations add InitialCreate -p src/Fenrix.IaCStudio.Infrastructure -s src/Fenrix.IaCStudio.Infrastructure</code>
/// See docs/12-database-design.md.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // A throwaway design-time connection string; the real path is resolved at runtime.
        var dbPath = Path.Combine(Path.GetTempPath(), "fenrix-designtime.db");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new AppDbContext(options);
    }
}
