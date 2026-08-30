using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ten21.Business.Credits;
using Ten21.Domain.Common;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-37: "Apply Credits to Charges" -- a manual, PM-triggered draw-down of a unit's retained
/// overpayment credit (PaymentTransaction.UnallocatedAmount) against its outstanding Charges.
/// Deliberately a button, not a background job: there's no recurring-billing engine in this
/// codebase yet to hang a scheduled "anchor date" drawdown off of, and building one just for
/// this would be a large, unrelated undertaking -- the PM explicitly asked for a manual
/// trigger instead.
///
/// Business-layer refactor: all business logic AND all data access now live in CreditService
/// (Ten21.Business) -- this controller has no Ten21DbContext dependency at all.
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/credits")]
public class CreditsController : ControllerBase
{
    private readonly CreditService _creditService;

    public CreditsController(CreditService creditService)
    {
        _creditService = creditService;
    }

    [HttpPost("apply")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> ApplyCreditsToCharges(Guid propertyId, CancellationToken cancellationToken) =>
        Ok(await _creditService.ApplyCreditsToChargesAsync(propertyId, cancellationToken));
}
