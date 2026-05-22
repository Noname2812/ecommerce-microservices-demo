using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shared.Application;

namespace Shared.Messaging.Saga
{
    public abstract class SagaStateEfCoreConfiguration<TInstance>
        : IEntityTypeConfiguration<TInstance>
        where TInstance : class, ISagaState, ISagaVersion
    {
        protected abstract string TableName { get; }

        public void Configure(EntityTypeBuilder<TInstance> entity)
        {
            entity.ToTable(TableName);

            entity.HasKey(x => x.CorrelationId);
            entity.Property(x => x.CorrelationId).ValueGeneratedNever();

            entity.Property(x => x.CurrentState).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CreatedAt).IsRequired();
            entity.Property(x => x.UpdatedAt).IsRequired();
            entity.Property(x => x.Version).IsConcurrencyToken();

            ConfigureSaga(entity);
        }

        protected virtual void ConfigureSaga(EntityTypeBuilder<TInstance> builder) { }
    }
}
