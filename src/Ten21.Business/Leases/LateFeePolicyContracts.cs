using Ten21.Domain.Enums;

namespace Ten21.Business.Leases;

/// <summary>US-45 (Sprint 9): one late-fee policy per lease (zero-or-one). See
/// LateFeePolicy's own class comment for field semantics.</summary>
public record LateFeePolicyRequest(
    int GracePeriodDays,
    LateFeePolicyType PolicyType,
    decimal? BaseAmount,
    decimal? PercentageRate,
    decimal? DailyAccrualRate,
    decimal? MaxFeeCap);

public record LateFeePolicyResponse(
    Guid Id,
    Guid LeaseId,
    int GracePeriodDays,
    LateFeePolicyType PolicyType,
    decimal? BaseAmount,
    decimal? PercentageRate,
    decimal? DailyAccrualRate,
    decimal? MaxFeeCap);
