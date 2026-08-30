using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ten21.Business.Deposits;
using Ten21.Domain.Common;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Authorization;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-39: security deposit escrow -- collecting a deposit at move-in and settling it at
/// move-out. Kept as its own resource/controller (SecurityDeposit, not Charge or
/// PaymentTransaction) because deposit money is a liability held separately from operating
/// rental income, never rent actually received -- see SecurityDeposit's own class comment.
/// Same BOLA/IDOR-safe convention as every other ledger controller: nested under Property,
/// every action re-checks PropertyId == the route's propertyId.
///
/// Business-layer refactor: all business logic AND all data access now live in DepositService
/// (Ten21.Business) -- this controller has no Ten21DbContext dependency at all.
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/deposits")]
public class DepositsController : ControllerBase
{
    private readonly IAuthorizationService _authorizationService;
    private readonly DepositService _depositService;

    public DepositsController(IAuthorizationService authorizationService, DepositService depositService)
    {
        _authorizationService = authorizationService;
        _depositService = depositService;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetDeposits(Guid propertyId, CancellationToken cancellationToken) =>
        Ok(await _depositService.GetDepositsAsync(propertyId, cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetDeposit(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var notFoundMessage = $"Security deposit '{id}' was not found on this property.";
        var deposit = await _depositService.FindAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException(notFoundMessage);
        await _authorizationService.EnsureSameTenantAsync(User, deposit, notFoundMessage, cancellationToken);

        return Ok(await _depositService.BuildResponseAsync(deposit, cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> CollectDeposit(
        Guid propertyId, [FromBody] CollectDepositRequest request, CancellationToken cancellationToken)
    {
        var response = await _depositService.CollectDepositAsync(propertyId, request, cancellationToken);
        return CreatedAtAction(nameof(GetDeposit), new { propertyId, id = response.Id }, response);
    }

    [HttpPost("{id:guid}/settle")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> SettleDeposit(
        Guid propertyId, Guid id, [FromBody] SettleDepositRequest request, CancellationToken cancellationToken)
    {
        var notFoundMessage = $"Security deposit '{id}' was not found on this property.";
        var deposit = await _depositService.FindAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException(notFoundMessage);
        await _authorizationService.EnsureSameTenantAsync(User, deposit, notFoundMessage, cancellationToken);

        return Ok(await _depositService.SettleDepositAsync(deposit, propertyId, request, cancellationToken));
    }
}
