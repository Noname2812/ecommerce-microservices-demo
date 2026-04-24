using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Contract.Abstractions;

namespace Shared.Messaging
{

    /// <summary>
    /// MassTransit-backed implementation of IEventPublisher.
    /// Wraps IPublishEndpoint and adds structured logging + correlation propagation.
    /// </summary>
    internal sealed class EventPublisher : IEventPublisher
    {
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly ILogger<EventPublisher> _logger;

        public EventPublisher(IPublishEndpoint publishEndpoint, ILogger<EventPublisher> logger)
        {
            _publishEndpoint = publishEndpoint;
            _logger = logger;
        }

        public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : class, IIntegrationEvent
        {
            var eventName = typeof(TEvent).Name;

            _logger.LogInformation(
                "Publishing integration event {EventName} [{EventId}] from {Source}",
                eventName, @event.EventId, @event.Source);

            try
            {
                await _publishEndpoint.Publish(@event, ctx =>
                {
                    ctx.MessageId = @event.EventId;
                    if (@event.CorrelationId is not null)
                        ctx.CorrelationId = Guid.TryParse(@event.CorrelationId, out var cid) ? cid : null;
                    ctx.Headers.Set("x-source", @event.Source);
                    ctx.Headers.Set("x-causation-id", @event.CausationId ?? string.Empty);
                    ctx.Headers.Set("x-occurred-on", @event.OccurredOn.ToString("O"));
                }, cancellationToken);

                _logger.LogInformation(
                    "Successfully published {EventName} [{EventId}]", eventName, @event.EventId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to publish {EventName} [{EventId}]", eventName, @event.EventId);
                throw;
            }
        }

        public async Task PublishManyAsync<TEvent>(IEnumerable<TEvent> events, CancellationToken cancellationToken = default)
            where TEvent : class, IIntegrationEvent
        {
            foreach (var @event in events)
                await PublishAsync(@event, cancellationToken);
        }
    }

}
