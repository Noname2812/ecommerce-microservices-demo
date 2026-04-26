using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Identity.Domain.Models;
using UrbanX.Identity.Persistence.Constants;

namespace UrbanX.Identity.Persistence.Configurations;

internal sealed class AuthAuditLogConfiguration : IEntityTypeConfiguration<AuthAuditLog>
{
    public void Configure(EntityTypeBuilder<AuthAuditLog> builder)
    {
        builder.ToTable(TableNames.AuthAuditLogs);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.EventType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.UserAgent).HasMaxLength(512);
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.UserId).HasDatabaseName("ix_auth_audit_logs_user_id");
        builder.HasIndex(x => x.EventType).HasDatabaseName("ix_auth_audit_logs_event_type");
        builder.HasIndex(x => x.OccurredAt).HasDatabaseName("ix_auth_audit_logs_occurred_at");
    }
}
