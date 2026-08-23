namespace Ten21.Application.Abstractions;

/// <summary>
/// US-22: an explicit, per-request opt-out from the default soft-delete conversion that
/// AuditSaveChangesInterceptor otherwise applies to every ISoftDelete entity (US-07).
/// Scoped (same lifetime as ITenantContext) so a controller and the interceptor agree, for
/// this one request, on which specific entity INSTANCES should really be hard-deleted --
/// deliberately entity-instance-scoped rather than a global setting, so soft-delete stays
/// the safe, silent default for every other delete in the codebase. See
/// PropertiesController.DeleteProperty (US-22) for the one call site that needs this today.
/// </summary>
public interface IHardDeleteOverride
{
    void MarkForHardDelete(object entity);

    bool IsMarkedForHardDelete(object entity);
}
