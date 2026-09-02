using Ten21.Business.Billing;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-45 (Sprint 9): pure fee-amount math -- no DbContext. See
/// BillingCycleServiceTests for end-to-end assessment/idempotency/cap coverage.</summary>
public class LateFeeCalculatorTests
{
    private static LateFeePolicy Policy(
        LateFeePolicyType type, decimal? baseAmount = null, decimal? percentageRate = null,
        decimal? dailyAccrualRate = null, decimal? maxFeeCap = null) => new()
    {
        Id = Guid.NewGuid(),
        LeaseId = Guid.NewGuid(),
        GracePeriodDays = 5,
        PolicyType = type,
        BaseAmount = baseAmount,
        PercentageRate = percentageRate,
        DailyAccrualRate = dailyAccrualRate,
        MaxFeeCap = maxFeeCap,
    };

    [Fact]
    public void ComputeFee_Flat_ReturnsBaseAmount_RegardlessOfOverdueBalance()
    {
        var policy = Policy(LateFeePolicyType.Flat, baseAmount: 50m);

        Assert.Equal(50m, LateFeeCalculator.ComputeFee(policy, overdueBalance: 1450m));
    }

    [Fact]
    public void ComputeFee_Percentage_MultipliesOverdueBalanceByRate()
    {
        var policy = Policy(LateFeePolicyType.Percentage, percentageRate: 0.05m);

        Assert.Equal(72.50m, LateFeeCalculator.ComputeFee(policy, overdueBalance: 1450m));
    }

    [Fact]
    public void ComputeFee_DailyAccruing_ReturnsTheDailyRate()
    {
        var policy = Policy(LateFeePolicyType.DailyAccruing, dailyAccrualRate: 10m);

        Assert.Equal(10m, LateFeeCalculator.ComputeFee(policy, overdueBalance: 1450m));
    }

    [Fact]
    public void ComputeFee_Hybrid_CombinesBaseAmountAndPercentage()
    {
        var policy = Policy(LateFeePolicyType.Hybrid, baseAmount: 25m, percentageRate: 0.05m);

        Assert.Equal(97.50m, LateFeeCalculator.ComputeFee(policy, overdueBalance: 1450m));
    }

    [Fact]
    public void ApplyCap_ReturnsFullFee_WhenNoCapConfigured()
    {
        var policy = Policy(LateFeePolicyType.Flat, baseAmount: 50m, maxFeeCap: null);

        Assert.Equal(50m, LateFeeCalculator.ApplyCap(policy, proposedFee: 50m, alreadyAssessedTotal: 200m));
    }

    [Fact]
    public void ApplyCap_ReducesFee_ToTheRemainingCapacityUnderTheCap()
    {
        var policy = Policy(LateFeePolicyType.Flat, baseAmount: 50m, maxFeeCap: 120m);

        Assert.Equal(20m, LateFeeCalculator.ApplyCap(policy, proposedFee: 50m, alreadyAssessedTotal: 100m));
    }

    [Fact]
    public void ApplyCap_ReturnsZero_WhenTheCapIsAlreadyFullyConsumed()
    {
        var policy = Policy(LateFeePolicyType.Flat, baseAmount: 50m, maxFeeCap: 100m);

        Assert.Equal(0m, LateFeeCalculator.ApplyCap(policy, proposedFee: 50m, alreadyAssessedTotal: 100m));
    }
}
