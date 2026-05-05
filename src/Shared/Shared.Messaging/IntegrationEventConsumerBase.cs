using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Contract.Abstractions;

namespace Shared.Messaging
{

    /// <summary>
    /// Base class for MassTransit consumers that handle integration events
    /// by dispatching them as domain notifications via MediatR.
    /// Provides structured logging; transient exceptions are rethrown so you can opt in to
    /// per-endpoint <c>UseMessageRetry</c> (e.g. in <c>ConsumerDefinition.ConfigureConsumer</c>) — there is no bus-wide retry in <c>AddMessaging</c>.
    ///
    /// Usage:
    /// <code>
    /// public class OrderCreatedConsumer : IntegrationEventConsumerBase<OrderCreatedEvent>;
    /// {
    ///     public OrderCreatedConsumer(IMediator mediator, ILogger logger) : base(mediator, logger) {}
    /// }
    /// </code>
    /// </summary>
    public abstract class IntegrationEventConsumerBase<TEvent, TConsumer>
        : IConsumer<TEvent>
        where TEvent : class, IIntegrationEvent
    {
        private readonly IMediator _mediator;
        private readonly ILogger<TConsumer> _logger;

        protected IntegrationEventConsumerBase(
            IMediator mediator,
            ILogger<TConsumer> logger)
        {
            _mediator = mediator;
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
                    "Transient error processing {EventName} [{EventId}] — rethrown (use UseMessageRetry on the endpoint if retries are desired)",
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

        /// <summary>Override to implement your event handling logic.</summary>
        protected virtual Task HandleAsync(TEvent @event, CancellationToken cancellationToken)
            => _mediator.Publish(new IntegrationEventNotification<TEvent>(@event), cancellationToken);

        /// <summary>Override to classify which exceptions are transient and should trigger retry.</summary>
        protected virtual bool IsTransient(Exception ex) =>
            ex is TimeoutException or TaskCanceledException or OperationCanceledException;
    }

}
