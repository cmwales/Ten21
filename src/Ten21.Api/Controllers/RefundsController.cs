using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Credits;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Authorization;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-37: "Refund Credit Balance" -- an outbound disbursement of a resident's retained
/// overpayment credit. Separate from PaymentsController/CreditsController because
/// RefundTransaction is its own append-only resource (no update/delete, same convention as
/// PaymentTransaction), even though it draws down state (UnallocatedAmount) those controllers
/// also touch. US-39 (deposit settlement) will reuse this same entity with
/// Reason = DepositReturn via its own dedicated flow.
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/refunds")]
public class RefundsController : ControllerBase
{
    private readonly Ten21DbContext _dbContext;
    private readonly IInputSanitizer _sanitizer;
    private readonly IAuthorizationService _authorizationService;

    public RefundsController(Ten21DbContext dbContext, IInputSanitizer sanitizer, IAuthorizationService authorizationService)
    {
        _dbContext = dbContext;
        _sanitizer = sanitizer;
        _authorizationService = authorizationService;
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetRefund(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var refund = await _authorizationService.EnsureSameTenantAsync(
            User,
            await _dbContext.RefundTransactions.AsNoTracking()
                .FirstOrDefaultAsync(r => r.PropertyId == propertyId && r.Id == id, cancellationToken),
            $"Refund '{id}' was not found on this property.", cancellationToken);

        var residentName = await _dbContext.GetResidentNameAsync(refund.ResidentProfileId, cancellationToken);
        return Ok(ToResponse(refund, residentName));
    }

    /// <summary>
    /// Draws down the resident's available credit oldest-payment-first (FIFO) across their
    /// PaymentTransactions on this unit, up to the requested Amount, then records the
    /// disbursement. Rejected outright if the resident doesn't have enough retained credit --
    /// this endpoint only ever pays out money the unit is already holding for them, never
    /// creates new liability.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> RefundCreditBalance(
        Guid propertyId, [FromBody] RefundCreditBalanceRequest request, CancellationToken cancellationToken)
    {
        await _dbContext.EnsurePropertyExistsAsync(propertyId, cancellationToken);
        var referenceNumber = ValidateAndSanitize(request);

        var resident = await _dbContext.ResidentProfiles
            .FirstOrDefaultAsync(r => r.PropertyId == propertyId && r.Id == request.ResidentProfileId, cancellationToken)
            ?? throw new NotFoundException($"Resident '{request.ResidentProfileId}' was not found on this property.");

        var payments = await _dbContext.PaymentTransactions
            .Where(p => p.PropertyId == propertyId && p.ResidentProfileId == resident.Id && p.UnallocatedAmount > 0)
            .OrderBy(p => p.PaymentDate)
            .ToListAsync(cancellationToken);

        var availableCredit = payments.Sum(p => p.UnallocatedAmount);
        if (request.Amount > availableCredit)
        {
            throw new ConflictException(
                $"This resident only has ${availableCredit:0.00} in available credit on this unit.");
        }

        var remaining = request.Amount;
        foreach (var payment in payments)
        {
            if (remaining <= 0)
            {
                break;
            }

            var draw = Math.Min(remaining, payment.UnallocatedAmount);
            payment.UnallocatedAmount -= draw;
            remaining -= draw;
        }

        var refund = new RefundTransaction
        {
            Id = Guid.NewGuid(),
            ResidentProfileId = resident.Id,
            PropertyId = propertyId,
            Amount = request.Amount,
            RefundDate = request.RefundDate,
            TenderType = request.TenderType,
            ReferenceNumber = referenceNumber,
            Reason = RefundReason.OverpaymentRefund,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.RefundTransactions.Add(refund);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var residentName = $"{resident.FirstName} {resident.LastName}";
        return CreatedAtAction(nameof(GetRefund), new { propertyId, id = refund.Id }, ToResponse(refund, residentName));
    }

    private static RefundTransactionResponse ToResponse(RefundTransaction refund, string residentName) => new(
        refund.Id, refund.ResidentProfileId, residentName, refund.PropertyId, refund.Amount,
        refund.RefundDate, refund.TenderType, refund.ReferenceNumber, refund.Reason, refund.CreatedAt);

    private string? ValidateAndSanitize(RefundCreditBalanceRequest request)
    {
        var referenceNumber = NullIfBlank(_sanitizer.Sanitize(request.ReferenceNumber));

        var errors = new Dictionary<string, string[]>();

        if (request.Amount <= 0)
        {
            errors[nameof(request.Amount)] = ["Amount must be greater than zero."];
        }

        if (referenceNumber is { Length: > 100 })
        {
            errors[nameof(request.ReferenceNumber)] = ["Reference number must be 100 characters or fewer."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return referenceNumber;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
