namespace Ten21.Domain.Common;

/// <summary>
/// Marker interface: entities implementing this get automatic change auditing via
/// AuditSaveChangesInterceptor. Deliberately opt-in, not blanket-applied to every entity in
/// the DbContext -- ASP.NET Core Identity's own tables (AccessFailedCount incrementing on
/// every failed login, SecurityStamp rotating, etc.) would otherwise flood the audit log
/// with framework noise instead of meaningful business changes.
/// </summary>
public interface IAuditableEntity
{
}

/// <summary>
/// Entities implementing this are never hard-deleted. AuditSaveChangesInterceptor
/// intercepts a Delete and converts it to a Modified update setting IsDeleted = true
/// instead; Ten21DbContext's global query filter then excludes IsDeleted == true rows from
/// normal queries automatically (combined with the tenant filter when an entity is also
/// ITenantScopedEntity).
/// </summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
}
