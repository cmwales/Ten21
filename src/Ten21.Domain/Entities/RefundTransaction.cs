using Ten21.Domain.Common;
using Ten21.Domain.Enums;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-37/US-39: an outbound disbursement to a resident -- either their unapplied overpayment
/// credit balance, or their remaining security deposit after a move-out settlement (US-39).
/// ResidentProfileId + PropertyId (renamed from the spec's ResidentId/UnitId to match this
/// codebase's existing naming) are both required, same dual-anchor convention as
/// PaymentTransaction: PropertyId for which unit's ledger this affects, ResidentProfileId for
/// who the money is actually going to.
///
/// Doesn't link back to which specific PaymentTransaction(s) supplied the refunded credit --
/// draw-down against a resident's available UnallocatedAmount happens oldest-payment-first at
/// refund time, but the refund record itself is just "this resident, this unit, this amount,
/// this reason," matching the spec's own schema. Append-only, same as PaymentTransaction --
/// no ISoftDelete, no update/delete action.
/// </summary>
public class RefundTransaction : ITenantScopedEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ResidentProfileId { get; set; }
    public Guid PropertyId { get; set; }

    public decimal Amount { get; set; }
    public DateOnly RefundDate { get; set; }
    public RefundTenderType TenderType { get; set; }
    public string? ReferenceNumber { get; set; }
    public RefundReason Reason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
