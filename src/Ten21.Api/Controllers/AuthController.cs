using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Auth;
using Ten21.Api.Contracts.Auth;
using Ten21.Application.Abstractions;
using Ten21.Domain.Entities;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Authorization;
using Ten21.Infrastructure.Identity;
using Ten21.Infrastructure.Middleware;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.RateLimiting;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-02: Identity & Refresh Token Pipeline.
///
/// Deliberately uses UserManager directly rather than SignInManager -- SignInManager is
/// built around cookie-based interactive sign-in flows; this is a stateless JWT API, and
/// UserManager.CheckPasswordAsync + manual AccessFailedAsync/ResetAccessFailedCountAsync
/// calls give the same brute-force lockout behavior (SECURITY.docx §5) without pulling in
/// cookie-auth-scheme machinery this API doesn't otherwise use.
///
/// [EnableRateLimiting] applies the 5-req/min-per-IP sliding window (US-05) to every action
/// in this controller, including /me -- SECURITY.docx's wording covers all of /api/auth/*
/// without carving out exceptions, and rate-limiting token refresh too is reasonable
/// defense against refresh-token-guessing attempts as well as login brute-forcing.
/// </summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting(AuthRateLimiterPolicy.PolicyName)]
public class AuthController : ControllerBase
{
    private const string GenericLoginFailureMessage = "Invalid email or password.";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly Ten21DbContext _dbContext;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IWebHostEnvironment _environment;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        Ten21DbContext dbContext,
        IJwtTokenService jwtTokenService,
        IRefreshTokenService refreshTokenService,
        IWebHostEnvironment environment)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _dbContext = dbContext;
        _jwtTokenService = jwtTokenService;
        _refreshTokenService = refreshTokenService;
        _environment = environment;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        // Same generic message regardless of WHICH check fails (no such user, wrong
        // password, inactive account) -- distinguishing them would let a caller enumerate
        // valid email addresses one login attempt at a time.
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedException(GenericLoginFailureMessage);
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            throw new UnauthorizedException(
                "Account is temporarily locked due to repeated failed login attempts.");
        }

        if (!await _userManager.CheckPasswordAsync(user, request.Password))
        {
            await _userManager.AccessFailedAsync(user); // counts toward the 5-attempt lockout
            throw new UnauthorizedException(GenericLoginFailureMessage);
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        var membership = await ResolvePrimaryMembershipAsync(user.Id, cancellationToken);
        if (membership is null)
        {
            throw new UnauthorizedException("This account has no property/tenant access configured.");
        }

        var response = await IssueTokensAsync(user.Id, membership, cancellationToken);
        return Ok(response);
    }

    [HttpPost("refresh-token")]
    [AllowAnonymous]
    public async Task<IActionResult> RefreshToken(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookie.CookieName, out var rawToken)
            || string.IsNullOrEmpty(rawToken))
        {
            throw new UnauthorizedException("No refresh token presented.");
        }

        RefreshTokenRotationResult rotation;
        try
        {
            rotation = await _refreshTokenService.ValidateAndRotateAsync(rawToken, GetClientIp(), cancellationToken);
        }
        catch (RefreshTokenException ex)
        {
            RefreshTokenCookie.Delete(Response);
            throw new UnauthorizedException(ex.Message);
        }

        // IgnoreQueryFilters is required and deliberate: no ITenantContext is resolved for
        // this request (there's no valid access token yet -- that's the entire point of
        // refresh), so the fail-closed filter from US-01 would otherwise return zero rows.
        var membership = await _dbContext.TenantMemberships
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                tm => tm.UserId == rotation.UserId && tm.TenantId == rotation.TenantId,
                cancellationToken);

        if (membership is null)
        {
            // Membership was revoked between issuance and this refresh (e.g. an admin
            // removed this person from the property). The old refresh token was already
            // consumed by ValidateAndRotateAsync above; deny the new access token too.
            RefreshTokenCookie.Delete(Response);
            throw new UnauthorizedException("Tenant access for this account has changed. Please log in again.");
        }

        var response = await BuildAuthResponseAsync(
            rotation.UserId, membership, rotation.NewRawToken, cancellationToken);
        return Ok(response);
    }

    [HttpPost("revoke-token")]
    [AllowAnonymous]
    public async Task<IActionResult> RevokeToken(CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue(RefreshTokenCookie.CookieName, out var rawToken)
            && !string.IsNullOrEmpty(rawToken))
        {
            await _refreshTokenService.RevokeAsync(rawToken, GetClientIp(), cancellationToken);
        }

        RefreshTokenCookie.Delete(Response);
        return NoContent();
    }

    /// <summary>
    /// Not part of US-02's literal acceptance criteria, but a near-universal need for any
    /// frontend consuming this API, and doubles as a live end-to-end check that
    /// PermissionClaimsTransformation (US-03) is actually expanding the role claim into a
    /// permission bundle -- the returned `permissions` array comes entirely from that
    /// transformation having already run before this action executes.
    /// </summary>
    [HttpGet("me")]
    public IActionResult Me()
    {
        var userId = User.FindFirst("user_id")?.Value;
        var tenantId = User.FindFirst(TenantMiddleware.TenantIdClaimType)?.Value;
        var organizationId = User.FindFirst(TenantMiddleware.OrganizationIdClaimType)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        var permissions = User.FindAll(PermissionClaimAuthorizationHandler.PermissionClaimType)
            .Select(c => c.Value)
            .ToList();

        return Ok(new
        {
            userId,
            tenantId,
            organizationId,
            role,
            permissions,
        });
    }

    private async Task<TenantMembership?> ResolvePrimaryMembershipAsync(Guid userId, CancellationToken cancellationToken)
    {
        var memberships = await _dbContext.TenantMemberships
            .IgnoreQueryFilters() // same bootstrap reasoning as the refresh-token lookup above
            .Where(tm => tm.UserId == userId)
            .ToListAsync(cancellationToken);

        return memberships.FirstOrDefault(m => m.IsPrimary) ?? memberships.FirstOrDefault();
    }

    private async Task<AuthResponse> IssueTokensAsync(
        Guid userId, TenantMembership membership, CancellationToken cancellationToken)
    {
        var rawRefreshToken = await _refreshTokenService.IssueAsync(
            userId, membership.TenantId, GetClientIp(), cancellationToken);

        return await BuildAuthResponseAsync(userId, membership, rawRefreshToken, cancellationToken);
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(
        Guid userId, TenantMembership membership, string rawRefreshToken, CancellationToken cancellationToken)
    {
        var role = await _roleManager.FindByIdAsync(membership.RoleId.ToString())
            ?? throw new InvalidOperationException($"Role {membership.RoleId} referenced by a TenantMembership no longer exists.");

        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters() // Tenant isn't ITenantScopedEntity, but be explicit rather than rely on that
            .SingleAsync(t => t.Id == membership.TenantId, cancellationToken);

        var accessToken = _jwtTokenService.GenerateAccessToken(
            userId, membership.TenantId, tenant.OrganizationId, role.Name!);

        RefreshTokenCookie.Set(Response, rawRefreshToken, _environment);

        return new AuthResponse(
            accessToken.Value, accessToken.ExpiresAtUtc, membership.TenantId, tenant.OrganizationId, role.Name!);
    }

    private string? GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
