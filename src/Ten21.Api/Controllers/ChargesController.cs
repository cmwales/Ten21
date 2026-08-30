using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ten21.Business.Charges;
using Ten21.Business.Statements;
using Ten21.Domain.Common;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Authorization;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-31/US-33/US-34/US-35 (renamed from ManualChargesController, Sprint 7): CRUD for
/// billable line items posted to a unit's ledger, plus the unit's full financial statement
/// (charges + payments + adjustments + dynamic balance). Nested under a Property, same
/// BOLA/IDOR-safe convention as LeasesController: every action re-checks PropertyId == the
/// route's propertyId rather than trusting a bare {id} lookup. Never scoped to a resident --
/// charges/payments are billed to the unit (tester feedback).
///
/// Business-layer refactor: all business logic AND all data access now live in ChargeService/
/// StatementService (Ten21.Business) -- this controller has no Ten21DbContext dependency at
/// all. It only resolves+authorizes the resource (IAuthorizationService.EnsureSameTenantAsync,
/// an ASP.NET Core-specific concern that stays here) and delegates.
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/charges")]
public class ChargesController : ControllerBase
{
    private readonly IAuthorizationService _authorizationService;
    private readonly ChargeService _chargeService;
    private readonly StatementService _statementService;

    public ChargesController(
        IAuthorizationService authorizationService, ChargeService chargeService, StatementService statementService)
    {
        _authorizationService = authorizationService;
        _chargeService = chargeService;
        _statementService = statementService;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetCharges(Guid propertyId, CancellationToken cancellationToken) =>
        Ok(await _chargeService.GetChargesAsync(propertyId, cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetCharge(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var notFoundMessage = $"Charge '{id}' was not found on this property.";
        var charge = await _chargeService.FindAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException(notFoundMessage);
        await _authorizationService.EnsureSameTenantAsync(User, charge, notFoundMessage, cancellationToken);

        return Ok(await _chargeService.BuildResponseAsync(charge, cancellationToken));
    }

    /// <summary>
    /// US-33: the unit's full financial statement -- every charge (with adjustments nested
    /// beneath it) and every payment, plus the dynamic running Balance. See
    /// UnitStatementResponse's own comment for the exact formula.
    /// </summary>
    [HttpGet("statement")]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetStatement(Guid propertyId, CancellationToken cancellationToken) =>
        Ok(await _statementService.BuildStatementAsync(propertyId, cancellationToken));

    /// <summary>
    /// US-40: renders the same statement GetStatement returns as a downloadable/embeddable
    /// PDF (see PaymentsController.GetReceipt's own comment on why "embeddable, not
    /// attachment"), with Charges/Payments filtered to the requested date range -- Balance
    /// itself is always the current snapshot regardless of range, same as the JSON view.
    /// </summary>
    [HttpGet("statement/pdf")]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetStatementPdf(
        Guid propertyId, [FromQuery] StatementDateRange range, CancellationToken cancellationToken)
    {
        var pdfBytes = await _statementService.BuildStatementPdfAsync(propertyId, range, cancellationToken);
        return File(pdfBytes, "application/pdf");
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> CreateCharge(
        Guid propertyId, [FromBody] UpsertChargeRequest request, CancellationToken cancellationToken)
    {
        var response = await _chargeService.CreateAsync(propertyId, request, cancellationToken);
        return CreatedAtAction(nameof(GetCharge), new { propertyId, id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> UpdateCharge(
        Guid propertyId, Guid id, [FromBody] UpsertChargeRequest request, CancellationToken cancellationToken)
    {
        var notFoundMessage = $"Charge '{id}' was not found on this property.";
        var charge = await _chargeService.FindAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException(notFoundMessage);
        await _authorizationService.EnsureSameTenantAsync(User, charge, notFoundMessage, cancellationToken);

        return Ok(await _chargeService.UpdateAsync(charge, request, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> DeleteCharge(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var notFoundMessage = $"Charge '{id}' was not found on this property.";
        var charge = await _chargeService.FindAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException(notFoundMessage);
        await _authorizationService.EnsureSameTenantAsync(User, charge, notFoundMessage, cancellationToken);

        await _chargeService.DeleteAsync(charge, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/void")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> VoidCharge(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var notFoundMessage = $"Charge '{id}' was not found on this property.";
        var charge = await _chargeService.FindAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException(notFoundMessage);
        await _authorizationService.EnsureSameTenantAsync(User, charge, notFoundMessage, cancellationToken);

        return Ok(await _chargeService.VoidAsync(charge, cancellationToken));
    }

    [HttpPost("{id:guid}/adjustments")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> CreateChargeAdjustment(
        Guid propertyId, Guid id, [FromBody] CreateChargeAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var notFoundMessage = $"Charge '{id}' was not found on this property.";
        var charge = await _chargeService.FindAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException(notFoundMessage);
        await _authorizationService.EnsureSameTenantAsync(User, charge, notFoundMessage, cancellationToken);

        var response = await _chargeService.CreateAdjustmentAsync(charge, request, cancellationToken);

        // Audit Refinement Sprint: was StatusCode(201, ...) -- no Location header, unlike
        // every other create endpoint in this codebase. Adjustments have no standalone
        // GET-by-id of their own (they're only ever read nested under their parent charge,
        // via GetCharge/GetStatement), so this points at the charge they now belong to.
        return CreatedAtAction(nameof(GetCharge), new { propertyId, id = charge.Id }, response);
    }
}
