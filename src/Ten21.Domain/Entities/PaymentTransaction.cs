using Ten21.Domain.Common;
using Ten21.Domain.Enums;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-34: a manually-logged payment received for a property (never a specific resident --
/// "we dont really care who made the payment", per the PM). Scoped to a Property so the
/// waterfall allocation knows which unit's outstanding Charges to apply it against; the
/// actual application is recorded in one or more PaymentAllocation rows, never here directly.
/// Live tokenized payment processing is deferred to Phase 2 -- this only records payments
/// received outside the app (cash, check, Zelle, Venmo, direct deposit).
///
/// No ISoftDelete / no delete or edit action this sprint -- a logged payment is an append-only
/// audit record; correcting a mistake happens via a ChargeAdjustment on the affected charge(s),
/// not by editing or erasing payment history. Add real voiding later if a concrete need
/// shows up, rather than building it speculatively now.
/// </summary>
public class PaymentTransaction : ITenantScopedEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PropertyId { get; set; }

    public DateOnly PaymentDate { get; set; }
    public decimal AmountPaid { get; set; }
    public TenderType TenderType { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }

    public ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();

    public DateTimeOffset CreatedAt { get; set; }
}
