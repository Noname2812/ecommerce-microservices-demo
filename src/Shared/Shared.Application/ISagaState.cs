using MassTransit;

namespace Shared.Application
{
    /// <summary>
    /// Marker interface for saga state instances (stored in DB via MassTransit).
    /// </summary>
    public interface ISagaState : SagaStateMachineInstance
    {
        string CurrentState { get; set; }
        DateTimeOffset CreatedAt { get; set; }
        DateTimeOffset UpdatedAt { get; set; }
    }
}
