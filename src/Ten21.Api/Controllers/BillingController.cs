using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ten21.Business.Billing;
using Ten21.Domain.Common;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-44 (Sprint 9): the recurring-charge/late-fee cycle for one tenant. Deliberately NOT
/// nested under a Property like every other ledger controller -- one cycle covers every
/// property/lease the caller's tenant has in one atomic run, so a propertyId route
/// parameter doesn't apply here at all. Tenant identity comes from the ambient
/// ITenantContext (JWT/X-Tenant-Id via TenantMiddleware), same as every tenant-scoped
/// query in this codebase.
///
/// Doubles as both a PM-facing manual trigger/backfill button (this policy) and the target
/// of an external nightly scheduler (added in US-45 via a separate internal-API-key auth
/// path) -- see docs/User_Stories_Sprint_9.md for the full rationale.
/// </summary>
[ApiController]
[Route("api/billing")]
public class BillingController : ControllerBase
{
    private readonly BillingCycleService _billingCycleService;

    public BillingController(BillingCycleService billingCycleService)
    {
        _billingCycleService = billingCycleService;
    }

    [HttpPost("run-cycle")]
    [Authorize(Policy = Permissions.Lease.Manage)]
    public async Task<IActionResult> RunCycle(CancellationToken cancellationToken) =>
        Ok(await _billingCycleService.RunCycleAsync(cancellationToken));
}
