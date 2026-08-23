using Microsoft.AspNetCore.Identity;
using Ten21.Domain.Common;

namespace Ten21.Infrastructure.Identity;

/// <summary>
/// Ensures the 9-tier role taxonomy (RoleNames.All) exists as ApplicationRole rows.
/// Idempotent -- safe to call on every startup, only creates roles that don't already
/// exist. Called from Program.cs's dev-only startup block (see the comment there for why
/// this stays Development-only rather than running in every environment: production
/// seeding is a deliberate, reviewed step, not an app-boot side effect, same principle as
/// the migration auto-apply).
/// </summary>
public static class RoleSeeder
{
    public static async Task SeedAsync(RoleManager<ApplicationRole> roleManager)
    {
        foreach (var roleName in RoleNames.All)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new ApplicationRole(roleName));
            }
        }
    }
}
