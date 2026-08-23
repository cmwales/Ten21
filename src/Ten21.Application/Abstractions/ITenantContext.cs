namespace Ten21.Application.Abstractions;

/// <summary>
/// Ambient, request-scoped view of "which tenant is this request allowed to touch."
/// Populated once per request by TenantMiddleware (Infrastructure), then read by:
///   - Ten21DbContext's global query filter (every query)
///   - Ten21DbContext.SaveChangesAsync (auto-populates TenantId on new rows)
///   - The RLS session interceptor (SET LOCAL app.current_tenant_id on the DB connection)
///
/// This is interfaced deliberately (unlike most of Infrastructure) because it has a real,
/// near-term reason to vary by call site: request-scoped in the Web API, but a background
/// job / seed script / admin tool needs a context it can set explicitly without a JWT in
/// play. A concrete class here would force every non-HTTP caller to fake an HTTP context.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The active tenant for this request/operation, or null if not yet resolved
    /// (e.g. public/anonymous endpoints, or before TenantMiddleware has run).
    /// </summary>
    Guid? TenantId { get; }

    /// <summary>
    /// The parent Organization the active TenantId belongs to, if any (PMC-managed tenants
    /// only). Used to validate context-switch requests in US-04.
    /// </summary>
    Guid? OrganizationId { get; }

    /// <summary>
    /// True once TenantId has been explicitly set. Deliberately NOT the same as
    /// "TenantId.HasValue" being checked ad-hoc everywhere -- this gives call sites one
    /// clear thing to assert on.
    /// </summary>
    bool IsResolved { get; }

    /// <summary>
    /// The authenticated caller for this request/operation, or null if unresolved.
    /// Added for US-07 (Audit Logging): AuditSaveChangesInterceptor needs to know who made
    /// a change, and this is the same ambient per-request context object TenantMiddleware
    /// already populates from JWT claims -- adding UserId here avoids introducing a
    /// parallel "current user" interface for what's conceptually the same kind of ambient
    /// request data.
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// Sets the active tenant for the remainder of this scope. Called exactly once per
    /// request by TenantMiddleware after validating JWT claims. Throws if called twice in
    /// the same scope -- tenant context must never be silently overwritten mid-request.
    /// </summary>
    void SetTenant(Guid tenantId, Guid? organizationId = null);

    /// <summary>
    /// Sets the authenticated user for the remainder of this scope. Same once-only
    /// semantics as SetTenant, for the same reason.
    /// </summary>
    void SetUser(Guid userId);
}
