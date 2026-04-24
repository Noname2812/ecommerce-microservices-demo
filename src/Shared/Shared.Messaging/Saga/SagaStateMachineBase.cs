using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Application;

namespace Shared.Messaging.Saga
{
    /// <summary>
    /// Base class for all MassTransit saga state machines in the system.
    /// Provides standardised logging, common state definitions, and
    /// helper methods for correlation / causation propagation.
    ///
    /// Usage:
    /// <code>
    /// public class OrderSagaStateMachine
    ///     : SagaStateMachineBase<OrderSagaState>
    /// {
    ///     public OrderSagaStateMachine(ILogger<OrderSagaStateMachine> logger)
    ///         : base(logger)
    ///     {
    ///         InstanceState(x => x.CurrentState);
    ///
    ///         // ... declare Events, States, transitions ...
    ///
    ///         RegisterStateLogging(); // ← MUST be last
    ///     }
    /// }
    /// </code>
    /// </summary>
    public abstract class SagaStateMachineBase<TInstance> : MassTransitStateMachine<TInstance>
        where TInstance : class, ISagaVersion, SagaStateMachineInstance, ISagaState
    {
        protected readonly ILogger Logger;

        // ── Standard states available in every saga ───────────────────────────
        // These are bound by MassTransit AFTER the subclass constructor runs,
        // so they must NOT be referenced inside this base constructor.
        public State Active { get; protected set; } = null!;
        public State Completed { get; protected set; } = null!;
        public State Faulted { get; protected set; } = null!;
        public State Compensating { get; protected set; } = null!;

        protected SagaStateMachineBase(ILogger logger)
        {
            Logger = logger;
            // ⚠️  Do NOT call WhenEnter here — States are still null at this point.
            //     Call RegisterStateLogging() at the END of the subclass constructor
            //     once all States, Events, and transitions have been declared.
        }

        // ── State-change logging ──────────────────────────────────────────────

        /// <summary>
        /// Registers WhenEnter logging hooks for all standard states.
        /// Call this as the LAST statement in your subclass constructor,
        /// after all State / Event / During declarations.
        /// </summary>
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

        // ── Instance helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Stamps UpdatedAt and increments Version on every state transition.
        /// Called automatically inside RegisterStateLogging().
        /// You may also call it explicitly inside individual transition handlers.
        /// </summary>
        protected void StampInstance(TInstance instance)
        {
            instance.UpdatedAt = DateTimeOffset.UtcNow;
            instance.Version++;
        }

        /// <summary>
        /// Sets CorrelationId on the saga instance from the incoming message context.
        /// Only assigns if the instance does not already have a non-empty CorrelationId.
        /// Call this inside your initial event handler (Initially / When).
        /// </summary>
        protected static void SetCorrelationContext<TMessage>(
            TInstance instance,
            ConsumeContext<TMessage> context)
            where TMessage : class
        {
            if (instance.CorrelationId == Guid.Empty)
                instance.CorrelationId = context.CorrelationId ?? Guid.NewGuid();
        }

        // ── Private helpers ──────────────────────────────────────────────────

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