using Ten21.Domain.Common;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-39: the application of (a portion of) a SecurityDeposit against one outstanding Charge
/// during settlement -- the deposit-money equivalent of PaymentAllocation/CreditAllocation,
/// but kept as its own entity/table rather than reusing either: a deposit isn't a payment (no
/// PaymentTransaction backs it) and settling it isn't "applying retained overpayment credit"
/// (US-37's CreditAllocation) -- it's a liability transfer that must never be counted as
/// rental income. Counts toward a Charge's AllocatedAmount/PaymentStatus/lock state exactly
/// like PaymentAllocation/CreditAllocation do (see ChargesController's own comment), but is
/// deliberately excluded from PaymentTransaction.AmountPaid/UnitStatementResponse's Payments
/// list -- Balance accounts for it via its own SumDepositSettlements term instead.
/// </summary>
public class DepositSettlementAllocation : ITenantScopedEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SecurityDepositId { get; set; }
    public Guid TargetChargeId { get; set; }
    public decimal AppliedAmount { get; set; }
    public DateOnly AppliedDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
