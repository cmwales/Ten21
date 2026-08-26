using Ten21.Domain.Common;
using Ten21.Domain.Enums;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-34: a manually-logged payment received for a property. Scoped to a Property so the
/// waterfall allocation knows which unit's outstanding Charges to apply it against; the
/// actual application is recorded in one or more PaymentAllocation rows, never here directly.
/// Live tokenized payment processing is deferred to Phase 2 -- this only records payments
/// received outside the app (cash, check, Zelle, Venmo, direct deposit).
///
/// ResidentProfileId is required (correcting an earlier "we don't care who made the payment"
/// call): money belongs to a specific human payee, not the unit. Needed for -- an overpayment
/// or a payment made before any charge exists becomes a credit owed to a *person*, who must
/// still be refundable after they transfer units or move out; co-tenants splitting one shared
/// charge need an explicit per-payer record, not just a unit total; and a departing co-tenant's
/// payment history has to stay attached to them, independent of the remaining occupant's
/// ongoing ledger. Charges stay unit-scoped (tester feedback, see Charge's own comment) --
/// only payments need a human owner, since only payments can generate a refundable credit.
///
/// No ISoftDelete / no delete or edit action this sprint -- a logged payment is an append-only
/// audit record; correcting a mistake happens via a ChargeAdjustment on the affected charge(s),
/// not by editing or erasing payment history. Add real voiding later if a concrete need
/// shows up, rather than building it speculatively now.
///
/// US-37: UnallocatedAmount is the retained overpayment credit this payment produced -- set
/// once at LogPayment time to whatever the statutory waterfall couldn't apply to any charge,
/// then drawn down over time by CreditAllocation rows as a PM runs "Apply Credits to Charges"
/// (a manual action, not a background job -- see CreditAllocation's own comment) or issues a
/// RefundTransaction against it. Genuinely mutable, unlike everything else on this entity --
/// it's a running balance, not a historical fact.
/// </summary>
public class PaymentTransaction : ITenantScopedEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PropertyId { get; set; }
    public Guid ResidentProfileId { get; set; }

    public DateOnly PaymentDate { get; set; }
    public decimal AmountPaid { get; set; }
    public TenderType TenderType { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public decimal UnallocatedAmount { get; set; }

    public ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();

    public DateTimeOffset CreatedAt { get; set; }
}
