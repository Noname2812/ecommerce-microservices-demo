using Microsoft.EntityFrameworkCore;
using Shared.Outbox;

namespace Shared.Outbox.EfCore
{

    /// <summary>
    /// Minimal EF Core DbContext for the outbox table.
    /// In services using the outbox, add this as a secondary context
    /// OR inherit from it in your application DbContext.
    ///
    /// For the dual-write pattern, use IOutboxWriter inside the same
    /// transaction as your aggregate changes.
    /// </summary>
    public class OutboxDbContext : DbContext
    {
        public OutboxDbContext(DbContextOptions<OutboxDbContext> options) : base(options) { }

        // Allow construction from derived contexts
        protected OutboxDbContext(DbContextOptions options) : base(options) { }

        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfiguration(new OutboxMessageEntityTypeConfiguration());
        }
    }
}
