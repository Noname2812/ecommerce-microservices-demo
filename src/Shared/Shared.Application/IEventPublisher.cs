using Shared.Contract.Abstractions;

namespace Shared.Application
{
    /// <summary>
    /// Abstraction for publishing integration events to the message bus.
    /// Implementations wrap MassTransit's IPublishEndpoint.
    /// </summary>
    public interface IEventPublisher
    {
        Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : class, IIntegrationEvent;

        Task PublishManyAsync<TEvent>(IEnumerable<TEvent> events, CancellationToken cancellationToken = default)
            where TEvent : class, IIntegrationEvent;
    }
}
