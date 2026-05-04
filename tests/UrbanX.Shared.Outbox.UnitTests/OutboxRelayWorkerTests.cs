using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Outbox;
using Shared.Outbox.DependencyInjection.Options;
using Shared.Outbox.EfCore;
using System.Text.Json;

namespace UrbanX.Shared.Outbox.UnitTests;

public sealed class OutboxRelayWorkerTests
{
    [Fact]
    public async Task ProcessMessageAsync_publish_success_marks_processed()
    {
        await using var provider = BuildInMemoryMassTransit();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            var dbOptions = new DbContextOptionsBuilder<TestOutboxDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var ctx = new TestOutboxDbContext(dbOptions);
            var repo = new OutboxRepository(
                ctx,
                NullLogger<OutboxRepository>.Instance,
                Options.Create(new OutboxOptions()));
            var worker = new OutboxRelayWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new OutboxOptions()),
                NullLogger<OutboxRelayWorker>.Instance);

            var t = typeof(RelayDto).AssemblyQualifiedName!;
            var payload = new RelayDto { Text = "ok" };
            var json = JsonSerializer.Serialize(payload, typeof(RelayDto), new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            var msg = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = t,
                Payload = json,
                Status = OutboxMessageStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            };
            ctx.OutboxMessages.Add(msg);
            await ctx.SaveChangesAsync();

            await worker.ProcessMessageAsync(msg, bus, repo, CancellationToken.None);

            var updated = await ctx.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == msg.Id);
            Assert.Equal(OutboxMessageStatus.Processed, updated.Status);
            Assert.NotNull(updated.ProcessedAt);
        }
        finally
        {
            await bus.StopAsync();
        }
    }

    [Fact]
    public async Task ProcessMessageAsync_type_name_not_resolvable_increments_retry_stays_pending()
    {
        var dbOptions = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var ctx = new TestOutboxDbContext(dbOptions);
        var repo = new OutboxRepository(
            ctx,
            NullLogger<OutboxRepository>.Instance,
            Options.Create(new OutboxOptions { MaxRetryAttempts = 5 }));
        var worker = new OutboxRelayWorker(
            Mock.Of<IServiceScopeFactory>(),
            Options.Create(new OutboxOptions { MaxRetryAttempts = 5 }),
            NullLogger<OutboxRelayWorker>.Instance);

        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "Totally.Unknown.Type, NonexistentAsm",
            Payload = "{}",
            Status = OutboxMessageStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        ctx.OutboxMessages.Add(msg);
        await ctx.SaveChangesAsync();

        await worker.ProcessMessageAsync(msg, Mock.Of<IPublishEndpoint>(), repo, CancellationToken.None);

        var updated = await ctx.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == msg.Id);
        Assert.Equal(OutboxMessageStatus.Pending, updated.Status);
        Assert.Equal(1, updated.RetryCount);
        Assert.NotNull(updated.LastError);
    }

    [Fact]
    public async Task ProcessMessageAsync_publish_throws_increments_retry_preserves_row()
    {
        var dbOptions = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var ctx = new TestOutboxDbContext(dbOptions);
        var repo = new OutboxRepository(
            ctx,
            NullLogger<OutboxRepository>.Instance,
            Options.Create(new OutboxOptions()));
        var worker = new OutboxRelayWorker(
            Mock.Of<IServiceScopeFactory>(),
            Options.Create(new OutboxOptions()),
            NullLogger<OutboxRelayWorker>.Instance);

        var t = typeof(RelayDto).AssemblyQualifiedName!;
        var payload = new RelayDto { Text = "ok" };
        var json = JsonSerializer.Serialize(payload, typeof(RelayDto), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = t,
            Payload = json,
            Status = OutboxMessageStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        ctx.OutboxMessages.Add(msg);
        await ctx.SaveChangesAsync();

        var failingBus = new ThrowingPublishEndpoint(new InvalidOperationException("broker down"));

        await worker.ProcessMessageAsync(msg, failingBus, repo, CancellationToken.None);

        var updated = await ctx.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == msg.Id);
        Assert.Equal(OutboxMessageStatus.Pending, updated.Status);
        Assert.Equal(1, updated.RetryCount);
        Assert.NotNull(updated.LastError);
    }

    private static ServiceProvider BuildInMemoryMassTransit()
    {
        var services = new ServiceCollection();
        services.AddMassTransit(x =>
        {
            x.UsingInMemory((context, cfg) =>
            {
                cfg.Message<RelayDto>(m => m.SetEntityName("relay-dto"));
            });
        });
        return services.BuildServiceProvider(true);
    }
}

/// <summary>Test contract for in-memory MassTransit publish.</summary>
public sealed class RelayDto
{
    public string? Text { get; set; }
}
