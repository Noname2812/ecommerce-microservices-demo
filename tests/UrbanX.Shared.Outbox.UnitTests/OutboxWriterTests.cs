using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Contract.Abstractions;
using Shared.Outbox;
using Shared.Outbox.DependencyInjection.Options;
using Shared.Outbox.EfCore;

namespace UrbanX.Shared.Outbox.UnitTests;

public sealed class OutboxWriterTests
{
    [Fact]
    public async Task AddAsync_inserts_pending_row_without_save_changes_from_writer()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using var ctx = new TestOutboxDbContext(options);
        var repo = new OutboxRepository(
            ctx,
            NullLogger<OutboxRepository>.Instance,
            Options.Create(new OutboxOptions()));
        var writer = new OutboxWriter(repo);

        var payloadType = typeof(SampleAddPayload).AssemblyQualifiedName!;
        await writer.AddAsync(payloadType, new SampleAddPayload { N = 7 });

        await ctx.SaveChangesAsync();

        var row = Assert.Single(ctx.OutboxMessages.Local);
        Assert.Equal(OutboxMessageStatus.Pending, row.Status);
        Assert.Equal(0, row.RetryCount);
        Assert.Equal(payloadType, row.Type);
        Assert.Contains("\"n\":7", row.Payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteAsync_sets_id_from_event_and_type_assembly_qualified_name()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using var ctx = new TestOutboxDbContext(options);
        var repo = new OutboxRepository(
            ctx,
            NullLogger<OutboxRepository>.Instance,
            Options.Create(new OutboxOptions()));
        var writer = new OutboxWriter(repo);

        var eventId = Guid.NewGuid();
        var correlation = Guid.NewGuid().ToString("N");
        var evt = new OutboxWriterTestEvent
        {
            EventId = eventId,
            CorrelationId = correlation
        };

        await writer.WriteAsync(evt);
        await ctx.SaveChangesAsync();

        var row = Assert.Single(ctx.OutboxMessages.Local);
        Assert.Equal(eventId, row.Id);
        Assert.Equal(typeof(OutboxWriterTestEvent).AssemblyQualifiedName, row.Type);
        Assert.Equal(correlation, row.CorrelationId);
        Assert.Contains("outbox-writer-test", row.Payload, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record OutboxWriterTestEvent : IntegrationEventBase
    {
        public override string Source => "outbox-writer-test";
    }

    private sealed class SampleAddPayload
    {
        public int N { get; set; }
    }
}
