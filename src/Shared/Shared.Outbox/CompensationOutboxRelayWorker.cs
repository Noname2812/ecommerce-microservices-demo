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
    /// Polls the compensation outbox on a longer interval than <see cref="OutboxRelayWorker"/> and sends to
    /// the dedicated exchange <see cref="CompensationOutboxOptions.CompensationEventsExchange"/> (not the default kebab-cased publish topology for order events).
    /// </summary>
    public sealed class CompensationOutboxRelayWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CompensationOutboxRelayWorker> _logger;
        private readonly CompensationOutboxOptions _options;

        public CompensationOutboxRelayWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<CompensationOutboxOptions> options,
            ILogger<CompensationOutboxRelayWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _options = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "CompensationOutboxRelayWorker started. BatchSize={BatchSize}, Interval={Interval}s, Exchange={Exchange}, MaxRetryAttempts={Max}",
                _options.BatchSize, _options.PollingIntervalSeconds, _options.CompensationEventsExchange, _options.MaxRetryAttempts);

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
                    _logger.LogError(ex, "Unhandled error in CompensationOutboxRelayWorker polling cycle");
                }

                await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
            }

            _logger.LogInformation("CompensationOutboxRelayWorker stopped.");
        }

        private async Task ProcessBatchAsync(CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var bus = scope.ServiceProvider.GetRequiredService<IBus>();
            var repository = scope.ServiceProvider.GetRequiredService<ICompensationOutboxRepository>();

            var sendEndpoint = await bus.GetSendEndpoint(
                new Uri($"exchange:{_options.CompensationEventsExchange}", UriKind.Absolute));
            var messages = await repository.GetUnprocessedAsync(_options.BatchSize, ct);

            if (messages.Count == 0) return;

            _logger.LogDebug("CompensationOutboxRelayWorker processing {Count} messages", messages.Count);

            foreach (var message in messages)
            {
                await ProcessMessageAsync(message, sendEndpoint, repository, ct);
            }
        }

        /// <summary>One send attempt; failures go through <see cref="ICompensationOutboxRepository.MarkAsFailedAsync"/>.</summary>
        internal async Task ProcessMessageAsync(
            CompensationOutboxMessage message,
            ISendEndpoint sendEndpoint,
            ICompensationOutboxRepository repository,
            CancellationToken ct)
        {
            try
            {
                var clrType = Type.GetType(message.Type);
                if (clrType is null)
                {
                    _logger.LogError(
                        "Cannot resolve type '{Type}' for CompensationOutboxMessage {MessageId}",
                        message.Type, message.Id);
                    await repository.MarkAsFailedAsync(message.Id, $"Cannot resolve type: {message.Type}", ct);
                    return;
                }

                var payload = JsonSerializer.Deserialize(
                    message.Payload, clrType, CompensationOutboxJsonSerializerOptions.Default);
                if (payload is null)
                {
                    await repository.MarkAsFailedAsync(message.Id, "Payload deserialization returned null", ct);
                    return;
                }

                await sendEndpoint.Send(payload, clrType, ctx =>
                {
                    ctx.MessageId = message.Id;
                    ctx.Headers.Set("x-compensation-outbox-relay", "true");
                }, ct);

                await repository.MarkAsProcessedAsync(message.Id, ct);

                _logger.LogDebug(
                    "Compensation outbox relayed {Type} [{MessageId}] to exchange {Exchange}",
                    message.Type, message.Id, _options.CompensationEventsExchange);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Compensation outbox send failed for message {MessageId} type {Type}",
                    message.Id, message.Type);
                await repository.MarkAsFailedAsync(message.Id, ex.Message, ct);
            }
        }
    }
}
