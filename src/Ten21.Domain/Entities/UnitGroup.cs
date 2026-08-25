using Ten21.Domain.Common;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-29: a reusable, workspace-scoped physical section/phase label (e.g. "North Wing",
/// "Phase 1") a Property Manager can assign to any Property row in the workspace via the
/// matrix editor. Deliberately independent of UnitTier -- physical location and pricing tier
/// are two unrelated dropdown choices on the same Property row, not a hierarchy.
/// </summary>
public class UnitGroup : ITenantScopedEntity, IAuditableEntity, ISoftDelete
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string GroupName { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
