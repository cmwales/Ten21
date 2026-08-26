namespace Ten21.Domain.Enums;

/// <summary>US-33: never stored -- computed at read time from
/// Sum(PaymentAllocation.AllocatedAmount) for a charge versus its net Amount (after
/// adjustments). Distinct from ChargeLifecycleStatus (Active/Voided), which IS a stored,
/// explicitly-set state.</summary>
public enum ChargePaymentStatus
{
    Unpaid,
    Partial,
    Paid,
}
