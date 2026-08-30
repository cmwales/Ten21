using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ten21.Business.Refunds;
using Ten21.Domain.Common;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Authorization;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-37: "Refund Credit Balance" -- an outbound disbursement of a resident's retained
/// overpayment credit. Separate from PaymentsController/CreditsController because
/// RefundTransaction is its own append-only resource (no update/delete, same convention as
/// PaymentTransaction), even though it draws down state (UnallocatedAmount) those controllers
/// also touch. US-39 (deposit settlement) reuses this same entity with Reason = DepositReturn
/// via its own dedicated flow (DepositService).
///
/// Business-layer refactor: all business logic AND all data access now live in RefundService
/// (Ten21.Business) -- this controller has no Ten21DbContext dependency at all.
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/refunds")]
public class RefundsController : ControllerBase
{
    private readonly IAuthorizationService _authorizationService;
    private readonly RefundService _refundService;

    public RefundsController(IAuthorizationService authorizationService, RefundService refundService)
    {
        _authorizationService = authorizationService;
        _refundService = refundService;
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetRefund(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var notFoundMessage = $"Refund '{id}' was not found on this property.";
        var refund = await _refundService.FindAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException(notFoundMessage);
        await _authorizationService.EnsureSameTenantAsync(User, refund, notFoundMessage, cancellationToken);

        return Ok(await _refundService.BuildResponseAsync(refund, cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> RefundCreditBalance(
        Guid propertyId, [FromBody] RefundCreditBalanceRequest request, CancellationToken cancellationToken)
    {
        var response = await _refundService.RefundCreditBalanceAsync(propertyId, request, cancellationToken);
        return CreatedAtAction(nameof(GetRefund), new { propertyId, id = response.Id }, response);
    }
}
