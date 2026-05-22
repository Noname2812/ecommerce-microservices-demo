using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Application;

namespace Shared.Messaging.Saga
{
    public abstract class SagaStateMachineBase<TInstance> : MassTransitStateMachine<TInstance>
        where TInstance : class, ISagaVersion, SagaStateMachineInstance, ISagaState
    {
        protected readonly ILogger Logger;

        public State Active { get; protected set; } = null!;
        public State Completed { get; protected set; } = null!;
        public State Faulted { get; protected set; } = null!;
        public State Compensating { get; protected set; } = null!;

        protected SagaStateMachineBase(ILogger logger)
        {
            Logger = logger;
        }

        protected void RegisterStateLogging()
        {
            WhenEnter(Active, x => x.Then(ctx =>
            {
                StampInstance(ctx.Saga);
                LogStateTransition(ctx.Saga, nameof(Active));
            }));

            WhenEnter(Completed, x => x.Then(ctx =>
            {
                StampInstance(ctx.Saga);
                LogStateTransition(ctx.Saga, nameof(Completed));
            }));

            WhenEnter(Faulted, x => x.Then(ctx =>
            {
                StampInstance(ctx.Saga);
                LogStateTransition(ctx.Saga, nameof(Faulted));
            }));

            WhenEnter(Compensating, x => x.Then(ctx =>
            {
                StampInstance(ctx.Saga);
                LogStateTransition(ctx.Saga, nameof(Compensating));
            }));
        }

        protected void StampInstance(TInstance instance)
        {
            instance.UpdatedAt = DateTimeOffset.UtcNow;
            instance.Version++;
        }

        protected async Task SafeCompensateAsync(string step, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex,
                    "Saga compensation step {Step} failed (best-effort); continuing chain",
                    step);
            }
        }

        protected static void SetCorrelationContext<TMessage>(
            TInstance instance,
            ConsumeContext<TMessage> context)
            where TMessage : class
        {
            if (instance.CorrelationId == Guid.Empty)
                instance.CorrelationId = context.CorrelationId ?? Guid.NewGuid();
        }

        private void LogStateTransition(TInstance instance, string stateName)
        {
            Logger.LogInformation(
                "Saga {SagaType} [{CorrelationId}] entered state {State} (v{Version}) at {Timestamp}",
                GetType().Name,
                instance.CorrelationId,
                stateName,
                instance.Version,
                instance.UpdatedAt);
        }
    }
}
