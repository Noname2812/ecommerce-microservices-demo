using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shared.Outbox.EfCore
{

    internal sealed class OutboxMessageEntityTypeConfiguration : IEntityTypeConfiguration<OutboxMessage>
    {
        public void Configure(EntityTypeBuilder<OutboxMessage> builder)
        {
            builder.ToTable("outbox_messages");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .ValueGeneratedNever();

            builder.Property(x => x.EventType)
                .HasMaxLength(500)
                .IsRequired();

            builder.Property(x => x.Payload)
                .HasColumnType("text")
                .IsRequired();

            builder.Property(x => x.CorrelationId)
                .HasMaxLength(200);

            builder.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(x => x.Error)
                .HasMaxLength(2000);

            // Index for efficient polling of pending messages
            builder.HasIndex(x => new { x.Status, x.NextRetryAt })
                .HasDatabaseName("ix_outbox_messages_status_retry");

            builder.HasIndex(x => x.CreatedAt)
                .HasDatabaseName("ix_outbox_messages_created_at");
        }
    }
}
