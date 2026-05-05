using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Inventory.Domain.Models;
using UrbanX.Inventory.Persistence.Constants;

namespace UrbanX.Inventory.Persistence.Configurations;

internal sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable(TableNames.ProcessedEvents);
        builder.HasKey(x => x.EventId);
        builder.Property(x => x.EventId).ValueGeneratedNever();
        builder.Property(x => x.EventType).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ProcessedAt).IsRequired();

        builder.HasIndex(x => x.ProcessedAt);
    }
}
