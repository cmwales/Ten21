using Ten21.Domain.Common;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-37: the draw-down of previously-unallocated overpayment credit (see
/// PaymentTransaction.UnallocatedAmount) against a charge that didn't exist yet, or wasn't
/// outstanding, when the original payment was logged. Distinct from PaymentAllocation, which
/// captures what the statutory waterfall applied at the moment a payment was logged --
/// CreditAllocation captures a later, separate event: a PM manually running "Apply Credits to
/// Charges" (deliberately not a scheduled background job -- there's no recurring-billing
/// engine to hang a schedule off of yet, and the PM wanted a manual trigger instead).
/// Both PaymentAllocation and CreditAllocation count toward a Charge's AllocatedAmount/
/// PaymentStatus/lock state -- see ChargesController's own comments.
/// </summary>
public class CreditAllocation : ITenantScopedEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SourcePaymentTransactionId { get; set; }
    public Guid TargetChargeId { get; set; }
    public decimal AppliedAmount { get; set; }
    public DateOnly AppliedDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
