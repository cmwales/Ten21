using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Auth;
using Ten21.Api.Contracts.Organization;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Identity;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-04: Parent Organization Hierarchy & Context Switching. US-26 (Portfolio Expansion)
/// added AddWorkspace to this same controller -- it's the source of the multi-tenant data
/// SwitchContext actually switches between.
/// Every action here is authenticated (no [AllowAnonymous]) -- unlike login/refresh, this
/// assumes an already-valid access token and an already-resolved ITenantContext.
/// </summary>
[ApiController]
[Route("api/organization")]
public class OrganizationController : ControllerBase
{
    private readonly Ten21DbContext _dbContext;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantStampOverride _tenantStampOverride;
    private readonly IInputSanitizer _sanitizer;
    private readonly IWebHostEnvironment _environment;

    public OrganizationController(
        Ten21DbContext dbContext,
        RoleManager<ApplicationRole> roleManager,
        IJwtTokenService jwtTokenService,
        IRefreshTokenService refreshTokenService,
        ITenantContext tenantContext,
        ITenantStampOverride tenantStampOverride,
        IInputSanitizer sanitizer,
        IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _roleManager = roleManager;
        _jwtTokenService = jwtTokenService;
        _refreshTokenService = refreshTokenService;
        _tenantContext = tenantContext;
        _tenantStampOverride = tenantStampOverride;
        _sanitizer = sanitizer;
        _environment = environment;
    }

    /// <summary>
    /// Lists every tenant the caller holds a TenantMembership in -- deliberately across ALL
    /// of the caller's tenants, not just the currently active one. This is the SAME
    /// bootstrap-style exception AuthController uses at login (IgnoreQueryFilters()), used
    /// here a second time for a different legitimate reason: filtered strictly by the
    /// CALLER'S OWN UserId, it never exposes another user's memberships, and
    /// tenant_memberships was already documented (sql/rls-policies.sql, US-02) as not
    /// carrying an RLS policy precisely because of this class of cross-tenant self-lookup.
    /// </summary>
    [HttpGet("tenants")]
    public async Task<IActionResult> GetTenants(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        var memberships = await _dbContext.TenantMemberships
            .IgnoreQueryFilters()
            .Where(tm => tm.UserId == userId)
            .Join(_dbContext.Tenants.IgnoreQueryFilters(),
                tm => tm.TenantId, t => t.Id,
                (tm, t) => new { Membership = tm, Tenant = t })
            .ToListAsync(cancellationToken);

        var results = new List<TenantMembershipSummary>();
        foreach (var m in memberships)
        {
            var role = await _roleManager.FindByIdAsync(m.Membership.RoleId.ToString());
            results.Add(new TenantMembershipSummary(
                m.Tenant.Id, m.Tenant.Name, m.Membership.IsPrimary, role?.Name ?? "Unknown"));
        }

        return Ok(results);
    }

    /// <summary>
    /// Mints a fresh, scoped JWT for a different tenant the caller already has membership
    /// in. Two independent checks -- deliberately both present, same defense-in-depth
    /// principle as the EF filter + Postgres RLS pairing elsewhere:
    ///   1. A TenantMembership row must exist for (caller, target tenant) -- the PRIMARY
    ///      authorization check; sufficient on its own to prove the caller may act there.
    ///   2. The target tenant's OrganizationId must match the CURRENT token's
    ///      organization_id claim, when the caller is operating under a PMC org at all --
    ///      catches a TenantMembership row that exists but points outside the caller's real
    ///      portfolio (e.g. a data-entry mistake), independent of whether #1 would already
    ///      have caught it.
    ///
    /// Also reissues the refresh-token cookie, scoped to the target tenant. RefreshToken is
    /// otherwise fixed to the tenant it was issued under (see its class comment in US-02) --
    /// without this, the newly-minted 15-minute access token from a switch would outlive
    /// its usefulness the moment the frontend's next silent refresh reverted the caller to
    /// their PRIMARY tenant instead of the one they switched to. RevokeAndReissueForTenantAsync
    /// mirrors RefreshTokenService.ValidateAndRotateAsync's rotation pattern (revoke old,
    /// chain-track via ReplacedByTokenId, issue new) so token-reuse detection still sees one
    /// continuous chain across the switch.
    /// </summary>
    [HttpPost("switch-context")]
    public async Task<IActionResult> SwitchContext(
        [FromBody] SwitchContextRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        // A list, not SingleOrDefaultAsync: a user can hold more than one role in the SAME
        // target tenant (SECURITY.docx's multi-role support; as of US-14 every self-service
        // registrant is both PropertyManager and PropertyOwner on their own workspace).
        // Prefer whichever membership is IsPrimary, same tie-break Login/RefreshToken use.
        var memberships = await _dbContext.TenantMemberships
            .IgnoreQueryFilters() // same reasoning as GetTenants above
            .Where(tm => tm.UserId == userId && tm.TenantId == request.TenantId)
            .ToListAsync(cancellationToken);

        var membership = memberships.FirstOrDefault(m => m.IsPrimary) ?? memberships.FirstOrDefault();

        if (membership is null)
        {
            // no membership at all for that tenant -- not authorized, full stop
            throw new ForbiddenException("You do not have access to the requested tenant.");
        }

        var targetTenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .SingleAsync(t => t.Id == request.TenantId, cancellationToken);

        if (_tenantContext.OrganizationId is { } currentOrgId && targetTenant.OrganizationId != currentOrgId)
        {
            throw new ForbiddenException("The requested tenant is outside your current organization.");
        }

        var role = await _roleManager.FindByIdAsync(membership.RoleId.ToString())
            ?? throw new InvalidOperationException(
                $"Role {membership.RoleId} referenced by a TenantMembership no longer exists.");

        var accessToken = _jwtTokenService.GenerateAccessToken(
            userId, targetTenant.Id, targetTenant.OrganizationId, role.Name!);

        Request.Cookies.TryGetValue(RefreshTokenCookie.CookieName, out var oldRawToken);
        var newRawToken = await _refreshTokenService.RevokeAndReissueForTenantAsync(
            userId, targetTenant.Id, oldRawToken, GetClientIp(), cancellationToken);
        RefreshTokenCookie.Set(Response, newRawToken, _environment);

        return Ok(new
        {
            accessToken = accessToken.Value,
            expiresAtUtc = accessToken.ExpiresAtUtc,
            tenantId = targetTenant.Id,
            organizationId = targetTenant.OrganizationId,
            role = role.Name,
        });
    }

    /// <summary>
    /// US-26: Portfolio Expansion. An existing Property Manager creates another Tenant
    /// (workspace) under their own Organization -- the actual source of the second
    /// TenantMembership row SwitchContext needs to have anything to switch between.
    /// Establishes (or reuses) an Organization to parent both the caller's current tenant
    /// and the new one, retroactively promoting a standalone workspace into a portfolio's
    /// first member the first time this is called.
    ///
    /// Only a Property Manager on the CURRENT tenant may call this -- checked directly
    /// against the specific role, not just "any membership exists," since only the
    /// operator (not e.g. a Board Member) should be able to expand the portfolio.
    /// </summary>
    [HttpPost("workspaces")]
    public async Task<IActionResult> AddWorkspace(
        [FromBody] AddWorkspaceRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.WorkspaceName))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["WorkspaceName"] = ["Workspace name is required."],
            });
        }

        var userId = GetCurrentUserId();
        var currentTenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedException("No active tenant context.");

        var propertyManagerRole = await _roleManager.FindByNameAsync(RoleNames.PropertyManager)
            ?? throw new InvalidOperationException("PropertyManager role not seeded -- has RoleSeeder run?");

        var callerIsPropertyManagerHere = await _dbContext.TenantMemberships
            .IgnoreQueryFilters()
            .AnyAsync(
                tm => tm.UserId == userId && tm.TenantId == currentTenantId && tm.RoleId == propertyManagerRole.Id,
                cancellationToken);

        if (!callerIsPropertyManagerHere)
        {
            throw new ForbiddenException("Only a Property Manager may add another workspace to their portfolio.");
        }

        var currentTenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .SingleAsync(t => t.Id == currentTenantId, cancellationToken);

        Guid organizationId;
        if (currentTenant.OrganizationId is { } existingOrgId)
        {
            organizationId = existingOrgId;
        }
        else
        {
            var organization = new Organization
            {
                Id = Guid.NewGuid(),
                Name = $"{currentTenant.Name} Portfolio",
                // Placeholder, same reasoning as US-22's payment-ledger placeholder --
                // real subscription tiers are Phase 1 (Monetization & Billing) territory,
                // not invented speculatively here.
                SubscriptionTier = "Standard",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _dbContext.Organizations.Add(organization);
            currentTenant.OrganizationId = organization.Id;
            organizationId = organization.Id;
        }

        var newTenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = _sanitizer.Sanitize(request.WorkspaceName)!,
            OrganizationId = organizationId,
            PortfolioSize = request.PortfolioSize,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.Tenants.Add(newTenant);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Same "operator is also deed owner" grant AuthController.ProvisionWorkspaceAsync
        // makes for a brand-new self-registration -- the person expanding their own
        // portfolio is fully both on the new workspace too. IsPrimary stays false: the
        // caller already has a primary membership elsewhere, and SwitchContext (not this
        // endpoint) is what actually moves them into it.
        //
        // MarkTenantId is required here, not optional: this request's own ITenantContext is
        // still resolved to the CALLER'S CURRENT tenant (from their JWT), which is never
        // the brand-new tenant these rows belong to -- see ITenantStampOverride's own doc
        // comment for why the normal auto-stamping can't do this on its own.
        var propertyOwnerRole = await _roleManager.FindByNameAsync(RoleNames.PropertyOwner)
            ?? throw new InvalidOperationException("PropertyOwner role not seeded -- has RoleSeeder run?");

        var propertyManagerMembership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = propertyManagerRole.Id,
            IsPrimary = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var propertyOwnerMembership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RoleId = propertyOwnerRole.Id,
            IsPrimary = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _tenantStampOverride.MarkTenantId(propertyManagerMembership, newTenant.Id);
        _tenantStampOverride.MarkTenantId(propertyOwnerMembership, newTenant.Id);

        _dbContext.TenantMemberships.Add(propertyManagerMembership);
        _dbContext.TenantMemberships.Add(propertyOwnerMembership);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetTenants),
            null,
            new TenantMembershipSummary(newTenant.Id, newTenant.Name, false, propertyManagerRole.Name!));
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst("user_id")?.Value
            ?? throw new InvalidOperationException("Authenticated request is missing the user_id claim.");
        return Guid.Parse(claim);
    }

    private string? GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
