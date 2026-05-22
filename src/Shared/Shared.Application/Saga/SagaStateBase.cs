using MassTransit;
using Shared.Application;

namespace Shared.Messaging.Saga
{
    public abstract class SagaStateBase : ISagaState, ISagaVersion
    {
        public Guid CorrelationId { get; set; }
        public string CurrentState { get; set; } = default!;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public int Version { get; set; }
    }
}
