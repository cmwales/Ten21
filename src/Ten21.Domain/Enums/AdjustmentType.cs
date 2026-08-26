namespace Ten21.Domain.Enums;

/// <summary>US-35: whether a ChargeAdjustment increases (Debit) or decreases (Credit) the
/// target charge's outstanding balance. Posted only against charges that already have
/// payments applied (locked) -- see ChargeAdjustment's own class comment.</summary>
public enum AdjustmentType
{
    CreditAdjustment,
    DebitAdjustment,
}
