
using Shared.Application;

namespace Shared.Messaging.Saga
{
    /// <summary>
    /// Base class providing default saga state fields.
    /// </summary>
    public abstract class SagaStateBase : ISagaState
    {
        public Guid CorrelationId { get; set; }
        public string CurrentState { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public int Version { get; set; }
    }
}
