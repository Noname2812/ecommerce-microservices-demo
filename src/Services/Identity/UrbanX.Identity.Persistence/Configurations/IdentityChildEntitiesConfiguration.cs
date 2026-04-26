using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrbanX.Identity.Persistence.Constants;

namespace UrbanX.Identity.Persistence.Configurations;

internal sealed class IdentityUserRoleConfiguration : IEntityTypeConfiguration<IdentityUserRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> builder)
    {
        builder.ToTable(TableNames.UserRoles);
        builder.HasKey(ur => new { ur.UserId, ur.RoleId });
    }
}

internal sealed class IdentityUserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder)
    {
        builder.ToTable(TableNames.UserClaims);
        builder.HasKey(uc => uc.Id);
        builder.Property(uc => uc.ClaimType).HasMaxLength(256);
        builder.Property(uc => uc.ClaimValue).HasMaxLength(1024);
    }
}

internal sealed class IdentityUserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder)
    {
        builder.ToTable(TableNames.UserLogins);
        builder.HasKey(ul => new { ul.LoginProvider, ul.ProviderKey });
        builder.Property(ul => ul.LoginProvider).HasMaxLength(128);
        builder.Property(ul => ul.ProviderKey).HasMaxLength(128);
        builder.Property(ul => ul.ProviderDisplayName).HasMaxLength(256);
    }
}

internal sealed class IdentityUserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder)
    {
        builder.ToTable(TableNames.UserTokens);
        builder.HasKey(ut => new { ut.UserId, ut.LoginProvider, ut.Name });
        builder.Property(ut => ut.LoginProvider).HasMaxLength(128);
        builder.Property(ut => ut.Name).HasMaxLength(128);
        builder.Property(ut => ut.Value).HasMaxLength(2048);
    }
}

internal sealed class IdentityRoleClaimConfiguration : IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> builder)
    {
        builder.ToTable(TableNames.RoleClaims);
        builder.HasKey(rc => rc.Id);
        builder.Property(rc => rc.ClaimType).HasMaxLength(256);
        builder.Property(rc => rc.ClaimValue).HasMaxLength(1024);
    }
}
