using Ten21.Domain.Common;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-29: a reusable, workspace-scoped pricing label (e.g. "Ocean View 2BR") a Property
/// Manager can assign to any Property row in the workspace via the matrix editor, instead of
/// setting DefaultRent/AccountingCode door-by-door. Assigning a tier to a Property is purely
/// a data-entry convenience -- DefaultRent seeds Property.TargetRent at assignment time, it
/// is not a live-recalculated relationship (a manager can still override a single door's
/// TargetRent afterward without it snapping back).
/// </summary>
public class UnitTier : ITenantScopedEntity, IAuditableEntity, ISoftDelete
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string TierName { get; set; }
    public decimal DefaultRent { get; set; }
    public string? AccountingCode { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
