using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Outbox;
using Shared.Outbox.DependencyInjection.Options;
using Shared.Outbox.EfCore;

namespace UrbanX.Shared.Outbox.UnitTests;

public sealed class OutboxRepositoryTests
{
    [Fact]
    public async Task MarkAsFailedAsync_increments_retry_until_max_then_failed()
    {
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var ctx = new TestOutboxDbContext(options);
        var id = Guid.NewGuid();
        ctx.OutboxMessages.Add(new OutboxMessage
        {
            Id = id,
            Type = "x",
            Payload = "{}",
            Status = OutboxMessageStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await ctx.SaveChangesAsync();

        var repo = new OutboxRepository(
            ctx,
            NullLogger<OutboxRepository>.Instance,
            Options.Create(new OutboxOptions { MaxRetryAttempts = 5 }));

        for (var i = 1; i <= 4; i++)
        {
            await repo.MarkAsFailedAsync(id, $"err{i}");
            var row = await ctx.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id);
            Assert.Equal(OutboxMessageStatus.Pending, row.Status);
            Assert.Equal(i, row.RetryCount);
            Assert.NotNull(row.NextRetryAt);
        }

        await repo.MarkAsFailedAsync(id, "final");
        var end = await ctx.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        Assert.Equal(OutboxMessageStatus.Failed, end.Status);
        Assert.Equal(5, end.RetryCount);
        Assert.Equal("final", end.LastError);
    }
}
