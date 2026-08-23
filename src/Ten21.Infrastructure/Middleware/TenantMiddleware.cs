using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Middleware;

/// <summary>
/// Populates ITenantContext for the current request from signed JWT claims ONLY.
///
/// Note on deviation from the literal US-01 acceptance criteria: the original wording says
/// "extracts tenant_id from JWT or headers." ARCHITECTURE later settles this more
/// specifically -- context switching mints a brand new scoped JWT rather than trusting a
/// client-supplied X-Tenant-Id header, specifically so a client can never forge tenant
/// access just by setting a header. This middleware enforces that decision: JWT claims are
/// the only source of truth.
///
/// Must be registered after UseAuthentication() (so HttpContext.User is populated) and
/// before endpoint execution.
/// </summary>
public class TenantMiddleware
{
    public const string TenantIdClaimType = "tenant_id";
    public const string OrganizationIdClaimType = "organization_id";

    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var tenantClaim = context.User?.FindFirst(TenantIdClaimType)?.Value;

        if (!string.IsNullOrEmpty(tenantClaim) && Guid.TryParse(tenantClaim, out var tenantId))
        {
            var orgClaim = context.User?.FindFirst(OrganizationIdClaimType)?.Value;
            Guid? organizationId = Guid.TryParse(orgClaim, out var parsedOrgId) ? parsedOrgId : null;

            tenantContext.SetTenant(tenantId, organizationId);
        }

        // Added for US-07 (Audit Logging): AuditSaveChangesInterceptor reads this to record
        // who made a change. Same claim JwtTokenService already issues as "user_id".
        // Deliberately independent of the tenant_id branch above (not nested inside it, as
        // it originally was): US-15/US-17's interim, tenant-less tokens (profile
        // completion, pending 2FA) still carry a real user_id worth recording -- a caller
        // holding one of those is genuinely authenticated, just not tenant-scoped yet.
        var userClaim = context.User?.FindFirst("user_id")?.Value;
        if (Guid.TryParse(userClaim, out var userId))
        {
            tenantContext.SetUser(userId);
        }

        // Anonymous/public endpoints (login, health checks, marketing routes) proceed with
        // an unresolved tenant context. There is no "query everything" fallback: any
        // tenant-scoped query with no resolved TenantId returns zero rows (fail-closed
        // filter in Ten21DbContext), and any write throws.
        await _next(context);
    }
}

public static class TenantMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
        => app.UseMiddleware<TenantMiddleware>();
}
