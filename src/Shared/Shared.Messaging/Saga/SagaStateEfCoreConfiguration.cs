using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shared.Application;

namespace Shared.Messaging.Saga
{
    /// <summary>
    /// Base EF Core IEntityTypeConfiguration for MassTransit saga state instances.
    /// Uses standard EF Core machinery (compatible with ApplyConfigurationsFromAssembly).
    /// CorrelationId is always the primary key, Version is the optimistic-concurrency token.
    ///
    /// Example:
    /// <code>
    /// internal sealed class PlaceSalesOrderSagaStateConfiguration
    ///     : SagaStateEfCoreConfiguration&lt;PlaceSalesOrderSagaState&gt;
    /// {
    ///     protected override string TableName => "place_sales_order_saga_states";
    ///
    ///     protected override void ConfigureSaga(EntityTypeBuilder&lt;PlaceSalesOrderSagaState&gt; builder)
    ///     {
    ///         builder.Property(x => x.OrderId).IsRequired();
    ///     }
    /// }
    /// </code>
    /// </summary>
    public abstract class SagaStateEfCoreConfiguration<TInstance>
        : IEntityTypeConfiguration<TInstance>
        where TInstance : class, ISagaState, ISagaVersion
    {
        /// <summary>Snake_case table name (e.g. "place_sales_order_saga_states").</summary>
        protected abstract string TableName { get; }

        public void Configure(EntityTypeBuilder<TInstance> entity)
        {
            entity.ToTable(TableName);

            entity.HasKey(x => x.CorrelationId);
            entity.Property(x => x.CorrelationId).ValueGeneratedNever();

            // Required in DB; in-memory new instances may have null CurrentState until the first transition.
            entity.Property(x => x.CurrentState).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CreatedAt).IsRequired();
            entity.Property(x => x.UpdatedAt).IsRequired();
            entity.Property(x => x.Version).IsConcurrencyToken();

            ConfigureSaga(entity);
        }

        /// <summary>Override to add saga-specific property mappings and indexes.</summary>
        protected virtual void ConfigureSaga(EntityTypeBuilder<TInstance> builder) { }
    }
}
