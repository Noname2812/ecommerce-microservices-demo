using MassTransit;

namespace Shared.Application
{
    public interface ISagaState : SagaStateMachineInstance
    {
        string CurrentState { get; set; }
        DateTimeOffset CreatedAt { get; set; }
        DateTimeOffset UpdatedAt { get; set; }
    }
}
