using MediatR;

namespace Shared.Application
{
    /// <summary>
    /// Domain event — stays within the bounded context, dispatched via MediatR.
    /// </summary>
    public interface IDomainEvent : INotification
    {
        Guid EventId { get; }
        DateTimeOffset OccurredOn { get; }
    }
}
