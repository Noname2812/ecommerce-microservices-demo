using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Contract.Abstractions;

namespace Shared.Messaging
{
    public abstract class IntegrationEventConsumerBase<TEvent, TConsumer>
        : IConsumer<TEvent>
        where TEvent : class, IIntegrationEvent
    {
        private readonly IMediator? _mediator;
        private readonly ILogger<TConsumer> _logger;

        protected IntegrationEventConsumerBase(
            IMediator mediator,
            ILogger<TConsumer> logger)
        {
            ArgumentNullException.ThrowIfNull(mediator);
            _mediator = mediator;
            _logger = logger;
        }

        protected IntegrationEventConsumerBase(ILogger<TConsumer> logger)
        {
            _mediator = null;
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<TEvent> context)
        {
            var eventName = typeof(TEvent).Name;
            var eventId = context.Message.EventId;

            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["EventId"] = eventId,
                ["EventName"] = eventName,
                ["CorrelationId"] = context.Message.CorrelationId ?? string.Empty
            });

            _logger.LogInformation("Received {EventName} [{EventId}]", eventName, eventId);

            try
            {
                await HandleAsync(context.Message, context.CancellationToken);

                _logger.LogInformation("Processed {EventName} [{EventId}]", eventName, eventId);
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                _logger.LogWarning(ex,
                    "Transient error processing {EventName} [{EventId}] — rethrown",
                    eventName, eventId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Fatal error processing {EventName} [{EventId}] — moving to dead-letter",
                    eventName, eventId);
                throw;
            }
        }

        protected virtual Task HandleAsync(TEvent @event, CancellationToken cancellationToken)
        {
            if (_mediator is null)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name} must override {nameof(HandleAsync)} when constructed with the logger-only constructor.");
            }

            return _mediator.Publish(new IntegrationEventNotification<TEvent>(@event), cancellationToken);
        }

        protected virtual bool IsTransient(Exception ex) =>
            ex is TimeoutException or TaskCanceledException or OperationCanceledException;
    }
}
