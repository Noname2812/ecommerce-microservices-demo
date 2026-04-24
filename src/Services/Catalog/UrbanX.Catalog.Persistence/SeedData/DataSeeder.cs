using Microsoft.EntityFrameworkCore;
using UrbanX.Catalog.Persistence;

namespace UrbanX.Catalog.Persistence.Seeding;

/// <summary>Optional dev/test seed entry point; call from host only when you add seed data.</summary>
public static class DataSeeder
{
    public static async Task SeedIfEmptyAsync(CatalogDbContext context, CancellationToken cancellationToken = default)
    {
        if (await context.Products.AnyAsync(cancellationToken))
            return;
        // Add categories, brands, or products here when you need a seeded catalog.
        await context.SaveChangesAsync(cancellationToken);
    }
}
