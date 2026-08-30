using Microsoft.AspNetCore.Mvc;
using Ten21.Api.Auth;
using Ten21.Business.Organizations;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-04: Parent Organization Hierarchy & Context Switching. US-26 (Portfolio Expansion)
/// added AddWorkspace to this same controller -- it's the source of the multi-tenant data
/// SwitchContext actually switches between.
/// Every action here is authenticated (no [AllowAnonymous]) -- unlike login/refresh, this
/// assumes an already-valid access token and an already-resolved ITenantContext.
///
/// Business-layer refactor: all business logic AND all data access now live in
/// OrganizationService (Ten21.Business) -- this controller only extracts the caller's
/// user_id claim, the refresh-token cookie, and the client IP (all ASP.NET Core-specific
/// concerns) and delegates.
/// </summary>
[ApiController]
[Route("api/organization")]
public class OrganizationController : ControllerBase
{
    private readonly OrganizationService _organizationService;
    private readonly IWebHostEnvironment _environment;

    public OrganizationController(OrganizationService organizationService, IWebHostEnvironment environment)
    {
        _organizationService = organizationService;
        _environment = environment;
    }

    [HttpGet("tenants")]
    public async Task<IActionResult> GetTenants(CancellationToken cancellationToken) =>
        Ok(await _organizationService.GetTenantsAsync(GetCurrentUserId(), cancellationToken));

    [HttpPost("switch-context")]
    public async Task<IActionResult> SwitchContext(
        [FromBody] SwitchContextRequest request, CancellationToken cancellationToken)
    {
        Request.Cookies.TryGetValue(RefreshTokenCookie.CookieName, out var oldRawToken);

        var result = await _organizationService.SwitchContextAsync(
            GetCurrentUserId(), request, oldRawToken, GetClientIp(), cancellationToken);

        RefreshTokenCookie.Set(Response, result.NewRawRefreshToken, _environment);

        return Ok(new
        {
            accessToken = result.AccessToken,
            expiresAtUtc = result.ExpiresAtUtc,
            tenantId = result.TenantId,
            organizationId = result.OrganizationId,
            role = result.Role,
        });
    }

    [HttpPost("workspaces")]
    public async Task<IActionResult> AddWorkspace(
        [FromBody] AddWorkspaceRequest request, CancellationToken cancellationToken)
    {
        var response = await _organizationService.AddWorkspaceAsync(GetCurrentUserId(), request, cancellationToken);
        return CreatedAtAction(nameof(GetTenants), null, response);
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst("user_id")?.Value
            ?? throw new InvalidOperationException("Authenticated request is missing the user_id claim.");
        return Guid.Parse(claim);
    }

    private string? GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
