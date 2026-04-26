using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Shared.Application.Authorization;
using UrbanX.Identity.Domain.Models;

namespace UrbanX.Identity.API.Configuration;

internal static class IdentitySeeder
{
    public static async Task SeedAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IConfiguration configuration,
        ILogger logger)
    {
        var roleDefinitions = new (string Name, string Description, string[] Permissions)[]
        {
            (Roles.Admin, "Administrator with full access", new[]
            {
                Permissions.Products.Read, Permissions.Products.Write,
                Permissions.Inventory.Read, Permissions.Inventory.Write,
                Permissions.Users.Read, Permissions.Users.Write, Permissions.Users.ManageRoles,
                Permissions.Roles.Read, Permissions.Roles.Write
            }),
            (Roles.Seller, "Merchant seller", new[]
            {
                Permissions.Products.Read, Permissions.Products.Write,
                Permissions.Inventory.Read, Permissions.Inventory.Write,
                Permissions.Users.Read
            }),
            (Roles.Customer, "End customer", new[]
            {
                Permissions.Products.Read,
                Permissions.Users.Read
            })
        };

        foreach (var (name, description, perms) in roleDefinitions)
        {
            var role = await roleManager.FindByNameAsync(name);
            if (role is null)
            {
                role = new ApplicationRole
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    NormalizedName = name.ToUpperInvariant(),
                    Description = description
                };
                await roleManager.CreateAsync(role);
                logger.LogInformation("Seeded role {Role}", name);
            }

            var existingClaims = (await roleManager.GetClaimsAsync(role))
                .Where(c => c.Type == "permission")
                .Select(c => c.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var p in perms.Where(p => !existingClaims.Contains(p)))
            {
                await roleManager.AddClaimAsync(role, new Claim("permission", p));
            }
        }

        var adminEmail = configuration["Seed:AdminEmail"] ?? "admin@urbanx.local";
        var adminPassword = configuration["Seed:AdminPassword"] ?? "Admin@123456";
        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = adminEmail,
                Email = adminEmail,
                DisplayName = "UrbanX Admin",
                EmailConfirmed = true,
                IsActive = true
            };
            var result = await userManager.CreateAsync(admin, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, Roles.Admin);
                logger.LogInformation("Seeded admin user {Email}", adminEmail);
            }
            else
            {
                logger.LogWarning("Failed to seed admin user: {Errors}",
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }
    }
}
