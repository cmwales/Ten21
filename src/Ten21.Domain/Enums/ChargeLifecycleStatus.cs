namespace Ten21.Domain.Enums;

/// <summary>US-35: a charge's own explicit lifecycle -- distinct from its PAYMENT status
/// (Unpaid/Partial/Paid), which is computed at read time from PaymentAllocations, not
/// stored. Voided is a genuine state a PM sets explicitly (only while unlocked, i.e. zero
/// dollars allocated); it is never derived from payment math.</summary>
public enum ChargeLifecycleStatus
{
    Active,
    Voided,
}
