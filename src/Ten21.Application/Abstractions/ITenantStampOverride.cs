namespace Ten21.Application.Abstractions;

/// <summary>
/// US-26: an explicit, per-request, per-entity-instance override for which TenantId
/// Ten21DbContext.ApplyTenantStamping should stamp on insert -- for the one legitimate case
/// where a caller's own ITenantContext (already resolved from their JWT for the tenant
/// they're currently acting in) is NOT the tenant a new row actually belongs to. Portfolio
/// expansion (OrganizationController.AddWorkspace) is the one call site that needs this
/// today: granting the caller a TenantMembership in the brand-new workspace they just
/// created, while their own request is still scoped to whichever tenant they called the
/// endpoint from.
///
/// Deliberately entity-instance-scoped (reference equality), same pattern as
/// IHardDeleteOverride -- without an explicit mark, ApplyTenantStamping's normal
/// fail-closed behavior (stamp from the active tenant context, or throw if none is
/// resolved) is completely unchanged for every other insert in the codebase. This is not a
/// way to bypass tenant isolation; it only ever redirects which tenant a controller has
/// ALREADY decided (server-side, having independently proven the caller may act there) a
/// specific new row belongs to -- never a client-supplied value.
/// </summary>
public interface ITenantStampOverride
{
    void MarkTenantId(object entity, Guid tenantId);

    Guid? GetOverride(object entity);
}
