using Ten21.Domain.Enums;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-45 (Sprint 9): one row per billing-cycle attempt, success or failure -- the
/// operational audit trail a site admin reads to know what ran, when, and why it failed.
/// Deliberately NOT ITenantScopedEntity -- same precedent as Tenant itself: this is a
/// platform-level record, queried across every tenant by SuperAdmin/internal tooling, not
/// filtered to "my own tenant" the way every other entity in this codebase is. Written
/// even on failure, in a save separate from the (possibly rolled-back) billing transaction
/// itself -- see BillingCycleService.RunCycleForTenantAsync.
/// </summary>
public class BillingCycleRun
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateOnly RunDate { get; set; }
    public BillingCycleRunStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public BillingCycleTrigger TriggeredBy { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
