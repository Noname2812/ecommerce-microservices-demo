

using MediatR;
using Shared.Contract.Abstractions;

namespace Shared.Messaging
{
    public record IntegrationEventNotification<TEvent>(TEvent Event) : INotification
        where TEvent : class, IIntegrationEvent;
}
