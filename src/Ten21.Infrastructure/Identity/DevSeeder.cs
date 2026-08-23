using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Infrastructure.Identity;

/// <summary>
/// Dev-only convenience seeding: one test Tenant, one test ApplicationUser, and a
/// TenantMembership binding them with the PropertyManager role -- so
/// POST /api/auth/login has something to actually authenticate against on a fresh
/// database, without hand-writing SQL. Never runs outside Development (see Program.cs),
/// and is a no-op if any user already exists, so it never touches real data.
///
/// This is explicitly a stopgap: there is no real user-registration/onboarding endpoint
/// yet (that's Phase 5, Zero-Touch Self Onboarding). Once that exists, delete this class
/// rather than extend it.
/// </summary>
public static class DevSeeder
{
    public const string TestEmail = "dev@ten21.io";
    public const string TestPassword = "Dev-Only-Passw0rd!1";

    public static async Task SeedAsync(
        Ten21DbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ITenantContext tenantContext)
    {
        if (await userManager.Users.AnyAsync())
        {
            return; // already seeded, or real users exist -- never overwrite either way
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Dev Test HOA",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        // Tenant is not ITenantScopedEntity (it sits above the tenant boundary, see its
        // class-level comment), so this insert needs no tenant context at all.
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = TestEmail,
            Email = TestEmail,
            EmailConfirmed = true,
            FirstName = "Dev",
            LastName = "User",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var createResult = await userManager.CreateAsync(user, TestPassword);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                "DevSeeder failed to create the test user: " +
                string.Join("; ", createResult.Errors.Select(e => e.Description)));
        }

        var role = await roleManager.FindByNameAsync(RoleNames.PropertyManager)
            ?? throw new InvalidOperationException("RoleSeeder must run before DevSeeder.");

        // TenantMembership DOES implement ITenantScopedEntity, so Ten21DbContext's
        // ApplyTenantStamping would refuse this insert with "no active tenant context is
        // set" -- correctly, since this code has no HTTP request/JWT to resolve one from
        // otherwise. This is the fail-closed design from US-01 working exactly as
        // intended, not a bug to route around: we explicitly set the context for this one
        // deliberate operation instead. tenantContext is resolved from the same DI scope
        // as dbContext (see Program.cs), so this actually takes effect on dbContext's
        // ambient state, not a disconnected instance.
        tenantContext.SetTenant(tenant.Id);

        dbContext.TenantMemberships.Add(new TenantMembership
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = user.Id,
            RoleId = role.Id,
            IsPrimary = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await dbContext.SaveChangesAsync();
    }
}
