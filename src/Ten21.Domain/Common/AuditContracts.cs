namespace Ten21.Domain.Common;

/// <summary>
/// Entities implementing this get automatic change auditing via AuditSaveChangesInterceptor,
/// PLUS a "who/when last touched this row" pair of columns the interceptor stamps
/// automatically -- CreatedByUserId at insert, UpdatedAt/UpdatedByUserId at every update
/// (including the soft-delete conversion, which IS an update). CreatedAt itself stays a
/// plain per-entity property, not part of this interface -- every implementer already sets
/// it explicitly at creation, and promoting it here would be a much larger, riskier change
/// (33+ existing call sites) for no benefit, whereas CreatedByUserId/UpdatedAt/
/// UpdatedByUserId are genuinely new fields nothing sets today.
///
/// Deliberately opt-in, not blanket-applied to every entity in the DbContext -- ASP.NET
/// Core Identity's own tables (AccessFailedCount incrementing on every failed login,
/// SecurityStamp rotating, etc.) would otherwise flood the audit log (and these new
/// columns) with framework noise instead of meaningful business changes.
///
/// Nullable throughout: CreatedByUserId can be null the same way AuditLog.ChangedByUserId
/// already is (background/seed operations with no resolved user context -- e.g. DevSeeder's
/// very first inserts); UpdatedAt/UpdatedByUserId stay null until an entity's first update
/// after creation.
/// </summary>
public interface IAuditableEntity
{
    Guid? CreatedByUserId { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
    Guid? UpdatedByUserId { get; set; }
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
