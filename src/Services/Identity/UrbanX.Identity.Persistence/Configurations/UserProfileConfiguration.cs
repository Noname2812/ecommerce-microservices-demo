using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Identity.Domain.Models;
using UrbanX.Identity.Persistence.Constants;

namespace UrbanX.Identity.Persistence.Configurations;

internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable(TableNames.UserProfiles);
        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).ValueGeneratedNever();

        builder.Property(p => p.Bio).HasMaxLength(1000);
        builder.Property(p => p.Gender).HasMaxLength(20);
        builder.Property(p => p.AddressLine).HasMaxLength(500);
        builder.Property(p => p.City).HasMaxLength(100);
        builder.Property(p => p.Country).HasMaxLength(100);
        builder.Property(p => p.PostalCode).HasMaxLength(20);
    }
}
