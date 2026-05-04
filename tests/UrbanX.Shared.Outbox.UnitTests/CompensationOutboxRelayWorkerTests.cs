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

public sealed class CompensationOutboxRelayWorkerTests
{
    [Fact]
    public async Task ProcessMessageAsync_send_success_marks_processed_and_deserializes_camel_case_payload()
    {
        object? sentMessage = null;
        var sendEndpoint = new Mock<ISendEndpoint>(MockBehavior.Strict);
        sendEndpoint
            .Setup(s => s.Send(
                It.IsAny<object>(),
                It.IsAny<Type>(),
                It.IsAny<IPipe<SendContext>>(),
                It.IsAny<CancellationToken>()))
            .Callback<object, Type, IPipe<SendContext>, CancellationToken>((body, _, _, _) =>
                sentMessage = body)
            .Returns(Task.CompletedTask);

        var dbOptions = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var ctx = new TestOutboxDbContext(dbOptions);
        var repo = new CompensationOutboxRepository(
            ctx,
            NullLogger<CompensationOutboxRepository>.Instance,
            Options.Create(new CompensationOutboxOptions()));
        var worker = new CompensationOutboxRelayWorker(
            Mock.Of<IServiceScopeFactory>(),
            Options.Create(new CompensationOutboxOptions()),
            NullLogger<CompensationOutboxRelayWorker>.Instance);

        var t = typeof(CompRelayDto).AssemblyQualifiedName!;
        var payload = new CompRelayDto { Text = "ok" };
        var json = JsonSerializer.Serialize(payload, typeof(CompRelayDto), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var msg = new CompensationOutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = t,
            Payload = json,
            Status = OutboxMessageStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        ctx.CompensationOutboxMessages.Add(msg);
        await ctx.SaveChangesAsync();

        await worker.ProcessMessageAsync(msg, sendEndpoint.Object, repo, CancellationToken.None);

        var dto = Assert.IsType<CompRelayDto>(sentMessage);
        Assert.Equal("ok", dto.Text);

        var updated = await ctx.CompensationOutboxMessages.AsNoTracking().SingleAsync(m => m.Id == msg.Id);
        Assert.Equal(OutboxMessageStatus.Processed, updated.Status);
        Assert.NotNull(updated.ProcessedAt);
        Assert.Null(updated.LastError);
    }

    [Fact]
    public async Task ProcessMessageAsync_type_name_not_resolvable_increments_retry_stays_pending()
    {
        var dbOptions = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var ctx = new TestOutboxDbContext(dbOptions);
        var repo = new CompensationOutboxRepository(
            ctx,
            NullLogger<CompensationOutboxRepository>.Instance,
            Options.Create(new CompensationOutboxOptions { MaxRetryAttempts = 5 }));
        var worker = new CompensationOutboxRelayWorker(
            Mock.Of<IServiceScopeFactory>(),
            Options.Create(new CompensationOutboxOptions { MaxRetryAttempts = 5 }),
            NullLogger<CompensationOutboxRelayWorker>.Instance);

        var msg = new CompensationOutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "Totally.Unknown.Type, NonexistentAsm",
            Payload = "{}",
            Status = OutboxMessageStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        ctx.CompensationOutboxMessages.Add(msg);
        await ctx.SaveChangesAsync();

        await worker.ProcessMessageAsync(msg, Mock.Of<ISendEndpoint>(), repo, CancellationToken.None);

        var updated = await ctx.CompensationOutboxMessages.AsNoTracking().SingleAsync(m => m.Id == msg.Id);
        Assert.Equal(OutboxMessageStatus.Pending, updated.Status);
        Assert.Equal(1, updated.RetryCount);
        Assert.Contains("Cannot resolve type", updated.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessMessageAsync_send_throws_increments_retry_preserves_row()
    {
        var dbOptions = new DbContextOptionsBuilder<TestOutboxDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var ctx = new TestOutboxDbContext(dbOptions);
        var repo = new CompensationOutboxRepository(
            ctx,
            NullLogger<CompensationOutboxRepository>.Instance,
            Options.Create(new CompensationOutboxOptions()));
        var worker = new CompensationOutboxRelayWorker(
            Mock.Of<IServiceScopeFactory>(),
            Options.Create(new CompensationOutboxOptions()),
            NullLogger<CompensationOutboxRelayWorker>.Instance);

        var t = typeof(CompRelayDto).AssemblyQualifiedName!;
        var payload = new CompRelayDto { Text = "ok" };
        var json = JsonSerializer.Serialize(payload, typeof(CompRelayDto), new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var msg = new CompensationOutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = t,
            Payload = json,
            Status = OutboxMessageStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        ctx.CompensationOutboxMessages.Add(msg);
        await ctx.SaveChangesAsync();

        var throwingSend = new ThrowingSendEndpoint(new InvalidOperationException("broker down"));

        await worker.ProcessMessageAsync(msg, throwingSend, repo, CancellationToken.None);

        var updated = await ctx.CompensationOutboxMessages.AsNoTracking().SingleAsync(m => m.Id == msg.Id);
        Assert.Equal(OutboxMessageStatus.Pending, updated.Status);
        Assert.Equal(1, updated.RetryCount);
        Assert.Contains("broker down", updated.LastError, StringComparison.Ordinal);
    }
}

public sealed class CompRelayDto
{
    public string? Text { get; set; }
}
