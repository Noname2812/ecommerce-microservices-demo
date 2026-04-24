using MediatR;

namespace Shared.Application
{
    public interface IDomainEventHandler<in TEvent> : INotificationHandler<TEvent>
       where TEvent : IDomainEvent
    {
    }
}
