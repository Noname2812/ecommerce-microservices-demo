using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shared.Application;

namespace Shared.Messaging.Saga
{
    /// <summary>
    /// Base EF Core entity type configuration for saga state instances.
    /// Inherit and extend this to configure your saga-specific columns.
    ///
    /// Example:
    /// <code>
    /// public class OrderSagaStateConfiguration
    ///     : SagaStateEfCoreConfiguration&lt;OrderSagaState&gt;
    /// {
    ///     protected override void ConfigureSaga(EntityTypeBuilder&lt;OrderSagaState&gt; builder)
    ///     {
    ///         builder.Property(x => x.OrderId).IsRequired();
    ///     }
    /// }
    /// </code>
    /// </summary>
    public abstract class SagaStateEfCoreConfiguration<TInstance>
        : SagaClassMap<TInstance>
        where TInstance : class, ISagaVersion, SagaStateMachineInstance, ISagaState
    {
        protected override void Configure(EntityTypeBuilder<TInstance> entity, ModelBuilder model)
        {
            entity.Property(x => x.CurrentState).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CreatedAt).IsRequired();
            entity.Property(x => x.UpdatedAt).IsRequired();
            entity.Property(x => x.Version).IsConcurrencyToken();

            ConfigureSaga(entity);
        }

        /// <summary>Override to add saga-specific property mappings.</summary>
        protected virtual void ConfigureSaga(EntityTypeBuilder<TInstance> builder) { }
    }
}
