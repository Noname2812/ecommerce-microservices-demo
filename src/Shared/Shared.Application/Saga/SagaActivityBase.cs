using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Application;

namespace Shared.Messaging.Saga
{
    public abstract class SagaActivityBase<TInstance, TData>
        : IStateMachineActivity<TInstance, TData>
        where TInstance : class, ISagaState
        where TData : class
    {
        protected readonly ILogger Logger;

        protected SagaActivityBase(ILogger logger) => Logger = logger;

        public void Probe(ProbeContext context) =>
            context.CreateScope(GetType().Name);

        public void Accept(StateMachineVisitor visitor) =>
            visitor.Visit(this);

        public async Task Execute(
            BehaviorContext<TInstance, TData> context,
            IBehavior<TInstance, TData> next)
        {
            Logger.LogDebug(
                "Executing {Activity} [{CorrelationId}]",
                GetType().Name, context.Saga.CorrelationId);
            try
            {
                await ExecuteAsync(context, next);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "Activity {Activity} failed [{CorrelationId}]",
                    GetType().Name, context.Saga.CorrelationId);
                throw;
            }
        }

        public Task Execute(
            BehaviorContext<TInstance> context,
            IBehavior<TInstance> next) => next.Execute(context);

        public Task Faulted<TException>(
            BehaviorExceptionContext<TInstance, TData, TException> context,
            IBehavior<TInstance, TData> next)
            where TException : Exception => next.Faulted(context);

        protected abstract Task ExecuteAsync(
            BehaviorContext<TInstance, TData> context,
            IBehavior<TInstance, TData> next);
    }
}
