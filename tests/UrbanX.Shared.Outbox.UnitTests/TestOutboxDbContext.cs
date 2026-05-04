using Microsoft.EntityFrameworkCore;
using Shared.Outbox.EfCore;

namespace UrbanX.Shared.Outbox.UnitTests;

public sealed class TestOutboxDbContext : OutboxDbContext
{
    public TestOutboxDbContext(DbContextOptions<TestOutboxDbContext> options)
        : base(options)
    {
    }
}
