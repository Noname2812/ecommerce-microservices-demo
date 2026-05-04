using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shared.Outbox.EfCore
{
    internal sealed class CompensationOutboxMessageEntityTypeConfiguration
        : IEntityTypeConfiguration<CompensationOutboxMessage>
    {
        public void Configure(EntityTypeBuilder<CompensationOutboxMessage> builder)
        {
            builder.ToTable("compensation_outbox");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Id)
                .ValueGeneratedNever();

            builder.Property(x => x.Type)
                .HasColumnName("EventType")
                .HasMaxLength(500)
                .IsRequired();

            builder.Property(x => x.Payload)
                .HasColumnType("text")
                .IsRequired();

            builder.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(x => x.LastError)
                .HasColumnName("Error")
                .HasMaxLength(2000);

            builder.HasIndex(x => new { x.Status, x.CreatedAt })
                .HasDatabaseName("ix_compensation_outbox_status_created_at");
        }
    }
}
