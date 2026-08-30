using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ten21.Application.Abstractions;
using Ten21.Business.Payments;
using Ten21.Domain.Common;
using Ten21.Infrastructure.Authorization;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-34: logging a manually-received payment against a property and running the statutory
/// waterfall allocation against that unit's outstanding Charges. Separate from
/// ChargesController because PaymentTransaction is its own resource (append-only, never
/// edited/deleted -- see that entity's own comment), even though both read from/write to the
/// same unit ledger. Same BOLA/IDOR-safe convention as ChargesController: nested under
/// Property, every action re-checks PropertyId == the route's propertyId.
///
/// Business-layer refactor: all business logic AND all data access now live in PaymentService
/// (Ten21.Business) -- this controller has no Ten21DbContext dependency at all. It only
/// resolves+authorizes the resource (IAuthorizationService.EnsureSameTenantAsync, an
/// ASP.NET Core-specific concern that stays here) and delegates.
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPdfService _pdfService;
    private readonly IAuthorizationService _authorizationService;
    private readonly PaymentService _paymentService;

    public PaymentsController(IPdfService pdfService, IAuthorizationService authorizationService, PaymentService paymentService)
    {
        _pdfService = pdfService;
        _authorizationService = authorizationService;
        _paymentService = paymentService;
    }

    /// <summary>
    /// US-40: a downloadable/embeddable PDF receipt for this one payment -- transaction date,
    /// tender type, reference number, and its allocated charges (see acceptance criteria).
    /// Returned inline (no Content-Disposition: attachment), on purpose, so the frontend can
    /// embed it directly in an &lt;iframe&gt; and let the browser's own PDF viewer supply
    /// download/print controls, rather than this codebase building a second HTML rendering of
    /// the same data.
    /// </summary>
    [HttpGet("{id:guid}/receipt")]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetReceipt(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var payment = await _authorizationService.EnsureSameTenantAsync(
            User, await _paymentService.FindAsync(propertyId, id, cancellationToken),
            $"Payment '{id}' was not found on this property.", cancellationToken);

        var pdfData = await _paymentService.BuildReceiptDataAsync(payment, propertyId, cancellationToken);
        var pdfBytes = _pdfService.GeneratePaymentReceipt(pdfData);
        return File(pdfBytes, "application/pdf");
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetPayment(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var payment = await _authorizationService.EnsureSameTenantAsync(
            User, await _paymentService.FindAsync(propertyId, id, cancellationToken),
            $"Payment '{id}' was not found on this property.", cancellationToken);

        return Ok(await _paymentService.BuildResponseAsync(payment, cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> LogPayment(
        Guid propertyId, [FromBody] LogPaymentRequest request, CancellationToken cancellationToken)
    {
        var response = await _paymentService.LogPaymentAsync(propertyId, request, cancellationToken);
        return CreatedAtAction(nameof(GetPayment), new { propertyId, id = response.Id }, response);
    }

    [HttpPost("{id:guid}/reverse")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> ReversePayment(
        Guid propertyId, Guid id, [FromBody] ReversePaymentRequest request, CancellationToken cancellationToken)
    {
        var payment = await _authorizationService.EnsureSameTenantAsync(
            User, await _paymentService.FindAsync(propertyId, id, cancellationToken),
            $"Payment '{id}' was not found on this property.", cancellationToken);

        return Ok(await _paymentService.ReverseAsync(payment, request, cancellationToken));
    }

    [HttpPost("{id:guid}/reallocate")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> ReallocatePayment(
        Guid propertyId, Guid id, [FromBody] ReallocatePaymentRequest request, CancellationToken cancellationToken)
    {
        var payment = await _authorizationService.EnsureSameTenantAsync(
            User, await _paymentService.FindAsync(propertyId, id, cancellationToken),
            $"Payment '{id}' was not found on this property.", cancellationToken);

        var response = await _paymentService.ReallocateAsync(payment, propertyId, request, cancellationToken);
        return CreatedAtAction(
            nameof(GetPayment), new { propertyId = request.TargetPropertyId, id = response.Id }, response);
    }
}
