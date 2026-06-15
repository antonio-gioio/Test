using AisStream.Api.Auth;
using AisStream.Api.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AisStream.Api.Data;

/// <summary>
/// Seeds the Admin role and an admin user from configuration on startup (ingestor only).
/// Idempotent: safe to run on every boot. Ensures the admin has the Enterprise tier and the
/// Admin role so the admin dashboard is reachable out of the box.
/// </summary>
public static class DataSeeder
{
    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = services.GetRequiredService<IOptions<AdminOptions>>().Value;

        if (!await roleManager.RoleExistsAsync(Roles.Admin))
        {
            await roleManager.CreateAsync(new IdentityRole(Roles.Admin));
            logger.LogInformation("Seeded '{Role}' role", Roles.Admin);
        }

        var user = await userManager.FindByEmailAsync(admin.Email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = admin.Email,
                Email = admin.Email,
                EmailConfirmed = true,
                Tier = SubscriptionTier.Enterprise,
            };
            var result = await userManager.CreateAsync(user, admin.Password);
            if (!result.Succeeded)
            {
                logger.LogWarning("Failed to seed admin user: {Errors}",
                    string.Join("; ", result.Errors.Select(e => e.Description)));
                return;
            }

            logger.LogInformation("Seeded admin user {Email}", admin.Email);
        }

        if (!await userManager.IsInRoleAsync(user, Roles.Admin))
        {
            await userManager.AddToRoleAsync(user, Roles.Admin);
        }
    }
}
