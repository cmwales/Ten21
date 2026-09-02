using Ten21.Domain.Common;
using Ten21.Domain.Enums;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-45 (Sprint 9): one late-fee policy per Lease (zero-or-one -- a lease without a row
/// here never gets late fees assessed). TenantId carried directly, same defense-in-depth
/// reasoning as every other tenant-scoped child entity in this codebase.
/// </summary>
public class LateFeePolicy : ITenantScopedEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LeaseId { get; set; }

    public int GracePeriodDays { get; set; } = 5;
    public LateFeePolicyType PolicyType { get; set; }

    public decimal? BaseAmount { get; set; }
    public decimal? PercentageRate { get; set; }
    public decimal? DailyAccrualRate { get; set; }

    /// <summary>Cumulative, not per-instance: once the sum of every LateFee charge posted
    /// against a lease's overdue balance reaches this, no further late fee posts until the
    /// balance is paid down. Null means uncapped.</summary>
    public decimal? MaxFeeCap { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
