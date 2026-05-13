using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UrbanX.Catalog.Persistence;

// For `dotnet ef`; override with env ConnectionStrings__catalogdb.
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CatalogDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__catalogdb")
            ?? "Host=localhost;Port=5432;Database=urbanx_catalog;Username=postgres;Password=postgres";
        optionsBuilder.UseNpgsql(connectionString);
        optionsBuilder.UseSnakeCaseNamingConvention();
        return new CatalogDbContext(optionsBuilder.Options);
    }
}
