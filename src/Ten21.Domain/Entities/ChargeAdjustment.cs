using Ten21.Domain.Common;
using Ten21.Domain.Enums;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-35: a credit or debit correction posted against a Charge that already has payments
/// applied (its base Amount is permanently locked at that point -- see Charge's own class
/// comment). Requires a mandatory Reason (5-250 chars) for audit-compliance: financial
/// history is never edited or deleted, only corrected with a new, explained entry.
///
/// No ISoftDelete, no update/delete action -- genuinely append-only, matching real accounting
/// practice (fixing a wrong adjustment means posting an offsetting one, not erasing the
/// original).
/// </summary>
public class ChargeAdjustment : ITenantScopedEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TargetChargeId { get; set; }

    public AdjustmentType AdjustmentType { get; set; }
    public decimal Amount { get; set; }
    public required string Reason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
