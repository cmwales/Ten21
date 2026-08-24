using System.IdentityModel.Tokens.Jwt;
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

/// <summary>US-26 (Portfolio Expansion) + US-27 (Switch-Context Test Coverage) --
/// OrganizationController had ZERO test coverage before this sprint despite being
/// security-sensitive (US-04, live since Phase 0). Same in-memory SQLite + minimal real
/// Identity DI pattern as ResidentsControllerTests/DirectoryControllerTests.</summary>
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
        bool callerIsPropertyManager = true, Guid? currentOrganizationId = null)
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

            // currentOrganizationId simulates a caller whose token was issued AFTER an
            // Organization was already established (e.g. a fresh login/switch following a
            // prior AddWorkspace/portfolio expansion) -- TenantContext.SetTenant can only be
            // called once per instance, so this can't be achieved by calling AddWorkspace
            // mid-test and expecting the controller's own already-resolved context to pick
            // up the change; it has to be seeded upfront instead, same as a real second
            // request would present it.
            if (currentOrganizationId is { } orgId && !seedDb.Organizations.IgnoreQueryFilters().Any(o => o.Id == orgId))
            {
                seedDb.Organizations.Add(new Organization { Id = orgId, Name = "Seeded Organization", SubscriptionTier = "Standard", CreatedAt = DateTimeOffset.UtcNow });
            }

            seedDb.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Riverside Portfolio HQ",
                PortfolioSize = 1,
                OrganizationId = currentOrganizationId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
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

        foreach (var roleName in new[] { RoleNames.PropertyManager, RoleNames.PropertyOwner, RoleNames.Tenant })
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
        tenantContext.SetTenant(tenantId, currentOrganizationId);
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

    [Fact]
    public async Task GetTenants_ListsEveryMembershipTheCallerHolds()
    {
        var (_, controller, tenantId, _) = CreateController();
        var added = await controller.AddWorkspace(new AddWorkspaceRequest("Second Property", 1), CancellationToken.None);
        var newTenantId = Assert.IsType<TenantMembershipSummary>(Assert.IsType<CreatedAtActionResult>(added).Value).TenantId;

        var result = await controller.GetTenants(CancellationToken.None);

        var summaries = Assert.IsAssignableFrom<IEnumerable<TenantMembershipSummary>>(Assert.IsType<OkObjectResult>(result).Value).ToList();
        // 1 (current, PropertyManager, primary) + 2 (new workspace: PropertyManager + PropertyOwner) = 3 rows.
        Assert.Equal(3, summaries.Count);
        Assert.Contains(summaries, s => s.TenantId == tenantId && s.IsPrimary);
        Assert.Contains(summaries, s => s.TenantId == newTenantId && s.Role == RoleNames.PropertyManager);
        Assert.Contains(summaries, s => s.TenantId == newTenantId && s.Role == RoleNames.PropertyOwner);
    }

    [Fact]
    public async Task SwitchContext_IssuesATokenScopedToTheTargetTenant()
    {
        var (_, controller, _, _) = CreateController();
        var added = await controller.AddWorkspace(new AddWorkspaceRequest("Second Property", 1), CancellationToken.None);
        var newTenantId = Assert.IsType<TenantMembershipSummary>(Assert.IsType<CreatedAtActionResult>(added).Value).TenantId;

        var result = await controller.SwitchContext(new SwitchContextRequest(newTenantId), CancellationToken.None);

        var (accessToken, tenantIdInBody) = ReadSwitchContextResponse(result);
        Assert.Equal(newTenantId, tenantIdInBody);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        Assert.Equal(newTenantId.ToString(), jwt.Claims.Single(c => c.Type == "tenant_id").Value);
        var roleClaim = jwt.Claims.Single(c => c.Type == ClaimTypes.Role).Value;
        Assert.True(roleClaim is RoleNames.PropertyManager or RoleNames.PropertyOwner);
    }

    /// <summary>SwitchContext returns an anonymous object -- anonymous types are `internal`
    /// by default, so `dynamic` member access from this (separate) test assembly fails at
    /// runtime with a RuntimeBinderException even though the properties are clearly there.
    /// Round-tripping through JSON sidesteps that C#-accessibility quirk entirely, and
    /// matches how every integration test in this codebase already reads a response body.</summary>
    private static (string AccessToken, Guid TenantId) ReadSwitchContextResponse(IActionResult result)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result).Value);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var accessToken = document.RootElement.GetProperty("accessToken").GetString()!;
        var tenantId = document.RootElement.GetProperty("tenantId").GetGuid();
        return (accessToken, tenantId);
    }

    [Fact]
    public async Task SwitchContext_ThrowsForbidden_WhenCallerHasNoMembershipInTheTargetTenant()
    {
        var (_, controller, _, _) = CreateController();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => controller.SwitchContext(new SwitchContextRequest(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task SwitchContext_ThrowsForbidden_WhenTargetTenantIsOutsideTheCallersCurrentOrganization()
    {
        // currentOrganizationId simulates a caller whose token already carries a resolved
        // Organization (the boundary check in SwitchContext only fires once the current
        // token's organization_id is non-null) -- see CreateController's own comment for
        // why this can't be achieved by calling AddWorkspace mid-test instead.
        var organizationId = Guid.NewGuid();
        var (db, controller, tenantId, userId) = CreateController(currentOrganizationId: organizationId);

        // A completely unrelated tenant (no Organization, or a DIFFERENT one) that the
        // caller ALSO happens to hold a real membership in -- e.g. a data-entry mistake, or
        // (per this sprint's own cross-PM reasoning) a resident/staff membership picked up
        // elsewhere. Membership alone (#1) would allow this switch; the org boundary (#2)
        // must still reject it.
        var pmRole = await db.Set<ApplicationRole>().IgnoreQueryFilters().FirstAsync(r => r.Name == RoleNames.PropertyManager);
        var seedTenantContext = new TenantContext();
        var unrelatedTenantId = Guid.NewGuid();
        using (var seedDb = new Ten21DbContext(
            new DbContextOptionsBuilder<Ten21DbContext>().UseSqlite(_connection).Options, seedTenantContext))
        {
            seedDb.Tenants.Add(new Tenant { Id = unrelatedTenantId, Name = "Unrelated Tenant", PortfolioSize = 1, CreatedAt = DateTimeOffset.UtcNow, OrganizationId = null });
            seedDb.SaveChanges();

            seedTenantContext.SetTenant(unrelatedTenantId);
        }
        using (var membershipDb = new Ten21DbContext(
            new DbContextOptionsBuilder<Ten21DbContext>().UseSqlite(_connection).Options, seedTenantContext))
        {
            membershipDb.TenantMemberships.Add(new TenantMembership
            {
                Id = Guid.NewGuid(),
                TenantId = unrelatedTenantId,
                UserId = userId,
                RoleId = pmRole.Id,
                IsPrimary = false,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            membershipDb.SaveChanges();
        }

        await Assert.ThrowsAsync<ForbiddenException>(
            () => controller.SwitchContext(new SwitchContextRequest(unrelatedTenantId), CancellationToken.None));
    }

    /// <summary>
    /// The concrete, provable version of the security question raised live during US-26:
    /// a caller who is PropertyManager on their current tenant and ONLY a Tenant (renter)
    /// on a different one must get a token that says "Tenant" -- never a role bled over
    /// from their other membership -- the moment they switch into that other workspace.
    /// </summary>
    [Fact]
    public async Task SwitchContext_ToAWorkspaceWhereCallerIsOnlyATenant_IssuesATenantRoleToken_NeverPropertyManager()
    {
        var (db, controller, _, userId) = CreateController();

        var tenantRole = await db.Set<ApplicationRole>().IgnoreQueryFilters().FirstAsync(r => r.Name == RoleNames.Tenant);
        var residentTenantId = Guid.NewGuid();
        var seedTenantContext = new TenantContext();
        using (var seedDb = new Ten21DbContext(
            new DbContextOptionsBuilder<Ten21DbContext>().UseSqlite(_connection).Options, seedTenantContext))
        {
            seedDb.Tenants.Add(new Tenant { Id = residentTenantId, Name = "A Different PM's Property", PortfolioSize = 1, CreatedAt = DateTimeOffset.UtcNow });
            seedDb.SaveChanges();
            seedTenantContext.SetTenant(residentTenantId);
        }
        using (var membershipDb = new Ten21DbContext(
            new DbContextOptionsBuilder<Ten21DbContext>().UseSqlite(_connection).Options, seedTenantContext))
        {
            membershipDb.TenantMemberships.Add(new TenantMembership
            {
                Id = Guid.NewGuid(),
                TenantId = residentTenantId,
                UserId = userId,
                RoleId = tenantRole.Id,
                IsPrimary = false,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            membershipDb.SaveChanges();
        }

        var result = await controller.SwitchContext(new SwitchContextRequest(residentTenantId), CancellationToken.None);

        var (accessToken, _) = ReadSwitchContextResponse(result);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        Assert.Equal(RoleNames.Tenant, jwt.Claims.Single(c => c.Type == ClaimTypes.Role).Value);
        Assert.Equal(residentTenantId.ToString(), jwt.Claims.Single(c => c.Type == "tenant_id").Value);
    }
}
