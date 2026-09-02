using Ten21.Domain.Entities;
using Ten21.Domain.Enums;

namespace Ten21.Business.Billing;

/// <summary>
/// US-45 (Sprint 9): pure late-fee-amount math, no DbContext -- BillingCycleService is the
/// only caller. Kept separate from BillingCycleService the same way RecurrenceSchedule is,
/// for direct unit testability.
/// </summary>
public static class LateFeeCalculator
{
    public static decimal ComputeFee(LateFeePolicy policy, decimal overdueBalance) => policy.PolicyType switch
    {
        LateFeePolicyType.Flat => policy.BaseAmount ?? 0m,
        LateFeePolicyType.Percentage => Math.Round(overdueBalance * (policy.PercentageRate ?? 0m), 2),
        LateFeePolicyType.DailyAccruing => policy.DailyAccrualRate ?? 0m,
        LateFeePolicyType.Hybrid => (policy.BaseAmount ?? 0m) + Math.Round(overdueBalance * (policy.PercentageRate ?? 0m), 2),
        _ => throw new ArgumentOutOfRangeException(nameof(policy.PolicyType)),
    };

    /// <summary>Caps a proposed fee against the policy's cumulative MaxFeeCap, given the
    /// total of every LateFee charge already posted against this overdue balance. Returns
    /// 0 once the cap is fully consumed -- the caller skips posting anything in that case.</summary>
    public static decimal ApplyCap(LateFeePolicy policy, decimal proposedFee, decimal alreadyAssessedTotal)
    {
        if (policy.MaxFeeCap is not { } cap)
        {
            return proposedFee;
        }

        var remaining = cap - alreadyAssessedTotal;
        return remaining <= 0 ? 0m : Math.Min(proposedFee, remaining);
    }
}
