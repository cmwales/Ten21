using Ten21.Domain.Common;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-34: the actual application of (a portion of) a PaymentTransaction to one Charge --
/// "payment gets applied to the charge." One payment can span several charges (the statutory
/// waterfall), and in principle one charge could receive allocations from several payments
/// over time (partial payments), so this is a genuine many-to-many join, not a scalar FK on
/// either side. Owned by PaymentTransaction for cascade-delete purposes (deleting a payment
/// -- not exposed this sprint, see PaymentTransaction's own comment -- would need to remove
/// its allocations too); Charge is referenced by scalar Id only, same
/// resolve-by-scalar-FK convention used everywhere else in this codebase.
/// </summary>
public class PaymentAllocation : ITenantScopedEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PaymentTransactionId { get; set; }
    public Guid ChargeId { get; set; }
    public decimal AllocatedAmount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
