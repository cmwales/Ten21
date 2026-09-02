using Ten21.Domain.Enums;

namespace Ten21.Business.Billing;

/// <summary>US-45 (Sprint 9): what the owner/operator site's scheduler enumerates before
/// looping POST api/billing/run-cycle/{tenantId} once per tenant.</summary>
public record TenantSummaryResponse(Guid Id, string Name);

/// <summary>US-45 (Sprint 9): one row of the operational audit trail -- what a SuperAdmin
/// (or the owner site) reads to know what ran, when, and why it failed.</summary>
public record BillingCycleRunResponse(
    Guid Id,
    Guid TenantId,
    DateOnly RunDate,
    BillingCycleRunStatus Status,
    string? ErrorMessage,
    BillingCycleTrigger TriggeredBy,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

/// <summary>Every filter is optional -- an unfiltered call returns everything, newest first.</summary>
public record BillingCycleRunFilter(BillingCycleRunStatus? Status, Guid? TenantId, DateOnly? FromDate, DateOnly? ToDate);
