namespace Ten21.Domain.Enums;

/// <summary>US-45 (Sprint 9): who/what initiated a BillingCycleRun -- Scheduled (the
/// internal-API-key-authenticated external scheduler), ManualRetry (a SuperAdmin retrying
/// a specific tenant after a failure), or Manual (a PM clicking the ledger page's own "Run
/// Billing Cycle" button for their own tenant).</summary>
public enum BillingCycleTrigger
{
    Manual,
    Scheduled,
    ManualRetry,
}
