using Microsoft.EntityFrameworkCore;
using Shared.Outbox.EfCore;

namespace UrbanX.Inventory.Persistence;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : OutboxDbContext(options)
{
    // DbSets sẽ được thêm khi có entity (migration-generator skill)

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(AssemblyReference.Assembly);
    }
}
