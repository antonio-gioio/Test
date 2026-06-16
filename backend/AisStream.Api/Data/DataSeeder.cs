using AisStream.Api.Auth;
using AisStream.Api.Ingestion;
using AisStream.Api.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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

        await SeedDefaultIntegrationAsync(services, logger);
    }

    /// <summary>
    /// Seeds a starter integration if none exist, so the app ingests data out of the box. Uses
    /// AisStream if a key is configured (legacy config), otherwise the built-in Simulator.
    /// </summary>
    private static async Task SeedDefaultIntegrationAsync(IServiceProvider services, ILogger logger)
    {
        var db = services.GetRequiredService<AppDbContext>();
        if (await db.Integrations.AnyAsync())
        {
            return;
        }

        var config = services.GetRequiredService<IConfiguration>();
        var aisStreamKey = config["Ais:AisStream:ApiKey"];

        var integration = string.IsNullOrWhiteSpace(aisStreamKey)
            ? new Integration { Name = "demo-simulator", Provider = AisProviderType.Simulator, Enabled = true }
            : new Integration
            {
                Name = "aisstream-default",
                Provider = AisProviderType.AisStream,
                Enabled = true,
                ApiKey = aisStreamKey,
            };

        db.Integrations.Add(integration);
        await db.SaveChangesAsync();
        logger.LogInformation("Seeded default integration '{Name}' ({Provider})",
            integration.Name, integration.Provider);
    }
}
