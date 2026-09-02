namespace Ten21.Domain.Enums;

/// <summary>US-45 (Sprint 9): the penalty formula a LateFeePolicy applies once a lease's
/// overdue BaseRent balance has passed its grace period. See LateFeeCalculator for the
/// actual per-type math.</summary>
public enum LateFeePolicyType
{
    Flat,
    Percentage,
    DailyAccruing,
    Hybrid,
}
