using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Shared.Outbox.Abstractions;
using System.Text.Json;
using Shared.Outbox.DependencyInjection.Options;

namespace Shared.Outbox
{

    /// <summary>
    /// Background worker (IHostedService) that polls the outbox table on a fixed interval,
    /// resolves the original event type, and publishes it to RabbitMQ via MassTransit.
    ///
    /// Designed to run as a single instance per service. For high-throughput scenarios,
    /// use a distributed lock (e.g. Redlock) before enabling multiple instances.
    /// </summary>
    public sealed class OutboxRelayWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OutboxRelayWorker> _logger;
        private readonly OutboxOptions _options;
        private readonly AsyncRetryPolicy _publishRetry;

        public OutboxRelayWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<OutboxOptions> options,
            ILogger<OutboxRelayWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _options = options.Value;

            // Polly retry: 3 attempts with exponential back-off before marking failed
            _publishRetry = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    onRetry: (ex, delay, attempt, _) =>
                        _logger.LogWarning(ex,
                            "Outbox publish retry {Attempt}/3 after {Delay}s", attempt, delay.TotalSeconds));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "OutboxRelayWorker started. BatchSize={BatchSize}, Interval={Interval}s",
                _options.BatchSize, _options.PollingIntervalSeconds);

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

        private async Task ProcessMessageAsync(
            OutboxMessage message,
            IPublishEndpoint publishEndpoint,
            IOutboxRepository repository,
            CancellationToken ct)
        {
            try
            {
                var eventType = Type.GetType(message.EventType);
                if (eventType is null)
                {
                    _logger.LogError(
                        "Cannot resolve type '{EventType}' for OutboxMessage {MessageId}",
                        message.EventType, message.Id);
                    await repository.MarkAsFailedAsync(message.Id, $"Cannot resolve type: {message.EventType}", ct);
                    return;
                }

                var payload = JsonSerializer.Deserialize(message.Payload, eventType);
                if (payload is null)
                {
                    await repository.MarkAsFailedAsync(message.Id, "Payload deserialization returned null", ct);
                    return;
                }

                await _publishRetry.ExecuteAsync(async () =>
                {
                    await publishEndpoint.Publish(payload, eventType, ctx =>
                    {
                        ctx.MessageId = message.Id;
                        if (message.CorrelationId is not null &&
                            Guid.TryParse(message.CorrelationId, out var cid))
                        {
                            ctx.CorrelationId = cid;
                        }
                        ctx.Headers.Set("x-outbox-relay", "true");
                    }, ct);
                });

                await repository.MarkAsProcessedAsync(message.Id, ct);

                _logger.LogDebug(
                    "Outbox relayed {EventType} [{MessageId}]", message.EventType, message.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to relay OutboxMessage {MessageId} of type {EventType}",
                    message.Id, message.EventType);
                await repository.MarkAsFailedAsync(message.Id, ex.Message, ct);
            }
        }
    }

}
