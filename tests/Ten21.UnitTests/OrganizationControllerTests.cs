using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ten21.Api.Contracts.Organization;
using Ten21.Api.Controllers;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Identity;
using Ten21.Infrastructure.Identity.Services;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Ten21.Infrastructure.Security;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-26: Portfolio Expansion (AddWorkspace). The rest of OrganizationController
/// (GetTenants/SwitchContext) is covered by US-27's dedicated test-coverage story -- this
/// file is scoped to the genuinely new logic this branch added. Same in-memory SQLite +
/// minimal real Identity DI pattern as ResidentsControllerTests/DirectoryControllerTests.
/// </summary>
public class OrganizationControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HtmlInputSanitizer _sanitizer = new();

    public OrganizationControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, OrganizationController Controller, Guid TenantId, Guid UserId) CreateController(
        bool callerIsPropertyManager = true)
    {
        var tenantContext = new TenantContext();
        var hardDeleteOverride = new HardDeleteOverride();
        var tenantStampOverride = new TenantStampOverride();

        var options = new DbContextOptionsBuilder<Ten21DbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(tenantContext, hardDeleteOverride))
            .Options;

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Seed the tenant + caller's membership using a throwaway db/context whose tenant
        // context IS resolved to this tenant -- CreateController's own controller-facing
        // TenantContext is intentionally left unresolved until after seeding, mirroring how
        // TenantMiddleware resolves it fresh from the JWT on a real request.
        var seedTenantContext = new TenantContext();
        seedTenantContext.SetTenant(tenantId);
        using (var seedDb = new Ten21DbContext(options, seedTenantContext))
        {
            seedDb.Database.EnsureCreated();
            seedDb.Tenants.Add(new Tenant { Id = tenantId, Name = "Riverside Portfolio HQ", PortfolioSize = 1, CreatedAt = DateTimeOffset.UtcNow });
            seedDb.SaveChanges();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<Ten21DbContext>()
            .AddDefaultTokenProviders();
        // AddEntityFrameworkStores resolves Ten21DbContext itself from DI -- register it
        // scoped to the SAME connection so role seeding lands in the same database the
        // controller's own db instance (created below) reads from.
        services.AddScoped<Ten21DbContext>(_ => new Ten21DbContext(options, new TenantContext()));
        var provider = services.BuildServiceProvider();
        var roleManager = provider.GetRequiredService<RoleManager<ApplicationRole>>();

        foreach (var roleName in new[] { RoleNames.PropertyManager, RoleNames.PropertyOwner })
        {
            if (!roleManager.RoleExistsAsync(roleName).GetAwaiter().GetResult())
            {
                roleManager.CreateAsync(new ApplicationRole(roleName)).GetAwaiter().GetResult();
            }
        }

        using (var seedDb = new Ten21DbContext(options, seedTenantContext))
        {
            // TenantMembership.UserId is a real FK to AspNetUsers -- a row must exist there
            // before any membership referencing it can be inserted.
            seedDb.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = $"{userId}@example.com",
                Email = $"{userId}@example.com",
                FirstName = "Test",
                LastName = "Caller",
                CreatedAt = DateTimeOffset.UtcNow,
            });

            if (callerIsPropertyManager)
            {
                var pmRole = roleManager.FindByNameAsync(RoleNames.PropertyManager).GetAwaiter().GetResult()!;
                seedDb.TenantMemberships.Add(new TenantMembership
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = userId,
                    RoleId = pmRole.Id,
                    IsPrimary = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }

            seedDb.SaveChanges();
        }

        // The controller's own db/tenantContext, resolved to the caller's current tenant
        // exactly as TenantMiddleware would from a real JWT -- separate instances from the
        // seed context above, matching a real request's fresh-scope-per-request lifetime.
        tenantContext.SetTenant(tenantId);
        var db = new Ten21DbContext(options, tenantContext, tenantStampOverride);

        var jwtConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "https://api.ten21.io",
                ["Jwt:Audience"] = "https://app.ten21.io",
                ["Jwt:Key"] = "unit-test-only-signing-key-do-not-reuse-anywhere-else",
            })
            .Build();

        var controller = new OrganizationController(
            db,
            roleManager,
            new JwtTokenService(jwtConfig),
            new RefreshTokenService(db),
            tenantContext,
            tenantStampOverride,
            _sanitizer,
            new FakeWebHostEnvironment())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("user_id", userId.ToString())], "TestAuth")),
                },
            },
        };

        return (db, controller, tenantId, userId);
    }

    private class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Ten21.UnitTests";
        public string WebRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public async Task AddWorkspace_CreatesNewTenant_AndGrantsPropertyManagerAndOwnerMembership()
    {
        var (db, controller, _, userId) = CreateController();

        var result = await controller.AddWorkspace(new AddWorkspaceRequest("Second Property", 3), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var summary = Assert.IsType<TenantMembershipSummary>(created.Value);
        Assert.Equal("Second Property", summary.TenantName);
        Assert.False(summary.IsPrimary);

        var memberships = await db.TenantMemberships
            .IgnoreQueryFilters()
            .Where(tm => tm.UserId == userId && tm.TenantId == summary.TenantId)
            .ToListAsync();
        Assert.Equal(2, memberships.Count); // PropertyManager + PropertyOwner
        Assert.All(memberships, m => Assert.False(m.IsPrimary));
    }

    [Fact]
    public async Task AddWorkspace_FirstExpansion_CreatesOrganization_AndPromotesCurrentTenant()
    {
        var (db, controller, tenantId, _) = CreateController();

        var result = await controller.AddWorkspace(new AddWorkspaceRequest("Second Property", 1), CancellationToken.None);
        var summary = Assert.IsType<TenantMembershipSummary>(Assert.IsType<CreatedAtActionResult>(result).Value);

        var currentTenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantId);
        var newTenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == summary.TenantId);

        Assert.NotNull(currentTenant.OrganizationId);
        Assert.Equal(currentTenant.OrganizationId, newTenant.OrganizationId);
        Assert.Equal(1, await db.Organizations.CountAsync());
    }

    [Fact]
    public async Task AddWorkspace_SecondExpansion_ReusesTheSameOrganization()
    {
        var (db, controller, _, _) = CreateController();

        var first = await controller.AddWorkspace(new AddWorkspaceRequest("Second Property", 1), CancellationToken.None);
        var second = await controller.AddWorkspace(new AddWorkspaceRequest("Third Property", 1), CancellationToken.None);

        var firstSummary = Assert.IsType<TenantMembershipSummary>(Assert.IsType<CreatedAtActionResult>(first).Value);
        var secondSummary = Assert.IsType<TenantMembershipSummary>(Assert.IsType<CreatedAtActionResult>(second).Value);

        var firstTenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == firstSummary.TenantId);
        var secondTenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == secondSummary.TenantId);

        Assert.Equal(firstTenant.OrganizationId, secondTenant.OrganizationId);
        Assert.Equal(1, await db.Organizations.CountAsync()); // still just one, not one per expansion
    }

    [Fact]
    public async Task AddWorkspace_ThrowsForbidden_WhenCallerIsNotPropertyManagerOnCurrentTenant()
    {
        var (_, controller, _, _) = CreateController(callerIsPropertyManager: false);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => controller.AddWorkspace(new AddWorkspaceRequest("Second Property", 1), CancellationToken.None));
    }

    [Fact]
    public async Task AddWorkspace_ThrowsValidationException_WhenWorkspaceNameIsMissing()
    {
        var (_, controller, _, _) = CreateController();

        await Assert.ThrowsAsync<ValidationException>(
            () => controller.AddWorkspace(new AddWorkspaceRequest("", 1), CancellationToken.None));
    }
}
