using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Outbox.Abstractions;
using System.Text.Json;
using Shared.Outbox.DependencyInjection.Options;

namespace Shared.Outbox
{

    /// <summary>
    /// Background worker (IHostedService) that polls the outbox table on a fixed interval,
    /// resolves the original event type, and publishes it to RabbitMQ via MassTransit.
    /// One publish attempt per message per cycle; failures increment <see cref="OutboxMessage.RetryCount"/>
    /// and defer via <see cref="OutboxMessage.NextRetryAt"/> until <see cref="OutboxOptions.MaxRetryAttempts"/>.
    ///
    /// Designed to run as a single instance per service. For high-throughput scenarios,
    /// use a distributed lock (e.g. Redlock) before enabling multiple instances.
    /// </summary>
    public sealed class OutboxRelayWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OutboxRelayWorker> _logger;
        private readonly OutboxOptions _options;

        public OutboxRelayWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<OutboxOptions> options,
            ILogger<OutboxRelayWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _options = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "OutboxRelayWorker started. BatchSize={BatchSize}, Interval={Interval}s, MaxRetryAttempts={Max}",
                _options.BatchSize, _options.PollingIntervalSeconds, _options.MaxRetryAttempts);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessBatchAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error in OutboxRelayWorker polling cycle");
                }

                await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
            }

            _logger.LogInformation("OutboxRelayWorker stopped.");
        }

        private async Task ProcessBatchAsync(CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

            var messages = await repository.GetUnprocessedAsync(_options.BatchSize, ct);

            if (messages.Count == 0) return;

            _logger.LogDebug("OutboxRelayWorker processing {Count} messages", messages.Count);

            foreach (var message in messages)
            {
                await ProcessMessageAsync(message, publishEndpoint, repository, ct);
            }
        }

        /// <summary>One publish attempt; failures go through <see cref="IOutboxRepository.MarkAsFailedAsync"/>.</summary>
        internal async Task ProcessMessageAsync(
            OutboxMessage message,
            IPublishEndpoint publishEndpoint,
            IOutboxRepository repository,
            CancellationToken ct)
        {
            try
            {
                var clrType = Type.GetType(message.Type);
                if (clrType is null)
                {
                    _logger.LogError(
                        "Cannot resolve type '{Type}' for OutboxMessage {MessageId}",
                        message.Type, message.Id);
                    await repository.MarkAsFailedAsync(message.Id, $"Cannot resolve type: {message.Type}", ct);
                    return;
                }

                var payload = JsonSerializer.Deserialize(message.Payload, clrType);
                if (payload is null)
                {
                    await repository.MarkAsFailedAsync(message.Id, "Payload deserialization returned null", ct);
                    return;
                }

                await publishEndpoint.Publish(payload, clrType, ctx =>
                {
                    ctx.MessageId = message.Id;
                    if (message.CorrelationId is not null &&
                        Guid.TryParse(message.CorrelationId, out var cid))
                    {
                        ctx.CorrelationId = cid;
                    }

                    ctx.Headers.Set("x-outbox-relay", "true");
                }, ct);

                await repository.MarkAsProcessedAsync(message.Id, ct);

                _logger.LogDebug(
                    "Outbox relayed {Type} [{MessageId}]", message.Type, message.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Outbox publish failed for message {MessageId} type {Type}",
                    message.Id, message.Type);
                await repository.MarkAsFailedAsync(message.Id, ex.Message, ct);
            }
        }
    }

}
