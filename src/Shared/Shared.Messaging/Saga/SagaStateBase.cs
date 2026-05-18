
using MassTransit;
using Shared.Application;

namespace Shared.Messaging.Saga
{
    /// <summary>
    /// Base class providing default saga state fields.
    /// Implements ISagaVersion explicitly so subclasses satisfy SagaClassMap&lt;T&gt; constraints.
    /// </summary>
    /// <remarks>
    /// <see cref="CurrentState"/> must be uninitialized (null) until MassTransit assigns the first state.
    /// Do not use <c>string.Empty</c> — MassTransit treats it as a named state and throws
    /// "state is not defined". EF persists the instance only after the first transition, when
    /// <see cref="CurrentState"/> is already set (see <see cref="SagaStateEfCoreConfiguration{TInstance}"/>).
    /// </remarks>
    public abstract class SagaStateBase : ISagaState, ISagaVersion
    {
        public Guid CorrelationId { get; set; }
        public string CurrentState { get; set; } = default!;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public int Version { get; set; }
    }
}
