using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shared.Outbox.EfCore;
using UrbanX.Identity.Domain.Models;

namespace UrbanX.Identity.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : OutboxDbContext(options)
{
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<ApplicationRole> Roles => Set<ApplicationRole>();
    public DbSet<IdentityUserRole<Guid>> UserRoles => Set<IdentityUserRole<Guid>>();
    public DbSet<IdentityUserClaim<Guid>> UserClaims => Set<IdentityUserClaim<Guid>>();
    public DbSet<IdentityUserLogin<Guid>> UserLogins => Set<IdentityUserLogin<Guid>>();
    public DbSet<IdentityUserToken<Guid>> UserTokens => Set<IdentityUserToken<Guid>>();
    public DbSet<IdentityRoleClaim<Guid>> RoleClaims => Set<IdentityRoleClaim<Guid>>();

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<AuthAuditLog> AuthAuditLogs => Set<AuthAuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(AssemblyReference.Assembly);
    }
}
