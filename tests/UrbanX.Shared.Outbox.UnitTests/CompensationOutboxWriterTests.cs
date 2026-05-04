using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Outbox;
using Shared.Outbox.DependencyInjection.Options;
using Shared.Outbox.EfCore;

namespace UrbanX.Shared.Outbox.UnitTests;

public sealed class CompensationOutboxWriterTests
{
    [Fact]
    public async Task AddAsync_inserts_pending_row_same_transaction_as_caller()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using var ctx = new TestOutboxDbContext(options);
        var repo = new CompensationOutboxRepository(
            ctx,
            NullLogger<CompensationOutboxRepository>.Instance,
            Options.Create(new CompensationOutboxOptions()));
        var writer = new CompensationOutboxWriter(repo);

        var payloadType = typeof(CompSamplePayload).AssemblyQualifiedName!;
        await writer.AddAsync(payloadType, new CompSamplePayload { N = 9 });

        await ctx.SaveChangesAsync();

        var row = Assert.Single(ctx.CompensationOutboxMessages.Local);
        Assert.Equal(OutboxMessageStatus.Pending, row.Status);
        Assert.Equal(0, row.RetryCount);
        Assert.Equal(payloadType, row.Type);
        Assert.Contains("\"n\":9", row.Payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddAsync_T_uses_assembly_qualified_name_and_serializes_payload()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using var ctx = new TestOutboxDbContext(options);
        var repo = new CompensationOutboxRepository(
            ctx,
            NullLogger<CompensationOutboxRepository>.Instance,
            Options.Create(new CompensationOutboxOptions()));
        var writer = new CompensationOutboxWriter(repo);

        await writer.AddAsync(new CompSamplePayload { N = 42 });

        await ctx.SaveChangesAsync();

        var row = Assert.Single(ctx.CompensationOutboxMessages.Local);
        Assert.Equal(typeof(CompSamplePayload).AssemblyQualifiedName, row.Type);
        Assert.Contains("\"n\":42", row.Payload, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CompSamplePayload
    {
        public int N { get; set; }
    }
}
