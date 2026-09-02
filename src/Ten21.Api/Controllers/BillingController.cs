using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ten21.Business.Billing;
using Ten21.Domain.Common;
using Ten21.Domain.Enums;
using Ten21.Infrastructure.Authorization;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-44/US-45 (Sprint 9): the recurring-charge/late-fee cycle. Deliberately NOT nested
/// under a Property like every other ledger controller -- one cycle covers every
/// property/lease the caller's tenant has in one atomic run. Two ways to trigger a run:
///
///   - POST run-cycle: the caller's OWN ambient tenant (JWT-derived via TenantMiddleware),
///     for a PM's manual "Run Billing Cycle" button.
///   - POST run-cycle/{tenantId}: an EXPLICIT tenant, for callers with no JWT for that
///     tenant -- a SuperAdmin retrying a specific tenant after a failure, or the future
///     owner/operator site's nightly scheduler (internal API key, no JWT at all).
///     TenantMiddleware only ever trusts signed JWT claims for tenant resolution (see its
///     own doc comment -- this codebase deliberately never trusts a client-supplied tenant
///     header), so this route sets ITenantContext explicitly instead, via
///     BillingCycleService.RunCycleForTenantAsync -- see docs/User_Stories_Sprint_9.md.
///
/// Both routes share one policy, Permissions.Billing.RunCycle -- deliberately distinct from
/// Permissions.Lease.Manage so a leaked internal API key can only ever trigger a billing
/// run, nothing else that permission would otherwise unlock. Satisfied by a PM/SuperAdmin's
/// normal permission claim OR the internal API key (InternalApiKeyAuthorizationHandler).
/// </summary>
[ApiController]
[Route("api/billing")]
public class BillingController : ControllerBase
{
    private readonly BillingCycleService _billingCycleService;
    private readonly BillingAdminService _billingAdminService;

    public BillingController(BillingCycleService billingCycleService, BillingAdminService billingAdminService)
    {
        _billingCycleService = billingCycleService;
        _billingAdminService = billingAdminService;
    }

    [HttpPost("run-cycle")]
    [Authorize(Policy = Permissions.Billing.RunCycle)]
    public async Task<IActionResult> RunCycle(CancellationToken cancellationToken) =>
        Ok(await _billingCycleService.RunCycleAsync(cancellationToken));

    [HttpPost("run-cycle/{tenantId:guid}")]
    [Authorize(Policy = Permissions.Billing.RunCycle)]
    public async Task<IActionResult> RunCycleForTenant(Guid tenantId, CancellationToken cancellationToken)
    {
        // Inferred from how this request authenticated, not a client-supplied flag -- a
        // caller can't claim to be "the scheduler" just by asking; it's Scheduled only if
        // it actually presented the internal key, ManualRetry (a human, via their own JWT)
        // otherwise.
        var usedInternalApiKey = Request.Headers.ContainsKey(InternalApiKeyAuthorizationHandler.ApiKeyHeaderName);
        var triggeredBy = usedInternalApiKey ? BillingCycleTrigger.Scheduled : BillingCycleTrigger.ManualRetry;

        return Ok(await _billingCycleService.RunCycleForTenantAsync(tenantId, triggeredBy, cancellationToken));
    }

    /// <summary>US-45: what the owner/operator site's scheduler enumerates before looping
    /// run-cycle/{tenantId} once per tenant. Deliberately the SAME policy as the trigger
    /// routes (not ViewRuns) -- the internal key already needs tenant ids to do its one job.</summary>
    [HttpGet("admin/tenants")]
    [Authorize(Policy = Permissions.Billing.RunCycle)]
    public async Task<IActionResult> ListTenants(CancellationToken cancellationToken) =>
        Ok(await _billingAdminService.ListTenantsAsync(cancellationToken));

    /// <summary>US-45: the operational audit trail -- SuperAdmin only (ViewRuns is
    /// deliberately not satisfiable by the internal API key, unlike RunCycle -- see
    /// Permissions.Billing's own doc comments).</summary>
    [HttpGet("admin/runs")]
    [Authorize(Policy = Permissions.Billing.ViewRuns)]
    public async Task<IActionResult> ListRuns(
        [FromQuery] BillingCycleRunStatus? status,
        [FromQuery] Guid? tenantId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken cancellationToken) =>
        Ok(await _billingAdminService.ListRunsAsync(new BillingCycleRunFilter(status, tenantId, fromDate, toDate), cancellationToken));
}
