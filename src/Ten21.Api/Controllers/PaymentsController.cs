using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Charges;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-34: logging a manually-received payment against a property and running the statutory
/// waterfall allocation against that unit's outstanding Charges. Separate from
/// ChargesController because PaymentTransaction is its own resource (append-only, never
/// edited/deleted -- see that entity's own comment), even though both read from/write to the
/// same unit ledger. Same BOLA/IDOR-safe convention as ChargesController: nested under
/// Property, every action re-checks PropertyId == the route's propertyId.
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/payments")]
public class PaymentsController : ControllerBase
{
    private readonly Ten21DbContext _dbContext;
    private readonly IInputSanitizer _sanitizer;

    public PaymentsController(Ten21DbContext dbContext, IInputSanitizer sanitizer)
    {
        _dbContext = dbContext;
        _sanitizer = sanitizer;
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetPayment(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var payment = await _dbContext.PaymentTransactions
            .Include(p => p.Allocations)
            .FirstOrDefaultAsync(p => p.PropertyId == propertyId && p.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Payment '{id}' was not found on this property.");

        var chargeDescriptionsById = await GetChargeDescriptionsAsync(payment.Allocations.Select(a => a.ChargeId), cancellationToken);
        var residentName = await GetResidentNameAsync(payment.ResidentProfileId, cancellationToken);
        return Ok(ToResponse(payment, residentName, chargeDescriptionsById));
    }

    /// <summary>
    /// US-34: creates the PaymentTransaction, then immediately allocates the full AmountPaid
    /// across the property's outstanding Charges in statutory priority order (Charge's own
    /// stored AllocationPriority, ascending -- lower number = paid first), oldest DueDate
    /// first within the same priority. Any amount left over once every outstanding charge is
    /// satisfied (an overpayment) is deliberately left unallocated: it still counts toward the
    /// unit's Balance via PaymentTransaction.AmountPaid (see UnitStatementResponse's balance
    /// formula), it just isn't tied to any one charge.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> LogPayment(
        Guid propertyId, [FromBody] LogPaymentRequest request, CancellationToken cancellationToken)
    {
        await EnsurePropertyExistsAsync(propertyId, cancellationToken);
        var fields = ValidateAndSanitize(request);

        var resident = await _dbContext.ResidentProfiles
            .FirstOrDefaultAsync(r => r.PropertyId == propertyId && r.Id == request.ResidentProfileId, cancellationToken)
            ?? throw new NotFoundException($"Resident '{request.ResidentProfileId}' was not found on this property.");

        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            ResidentProfileId = resident.Id,
            PaymentDate = request.PaymentDate,
            AmountPaid = request.AmountPaid,
            TenderType = request.TenderType,
            ReferenceNumber = fields.ReferenceNumber,
            Notes = fields.Notes,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.PaymentTransactions.Add(payment);

        var allocations = await BuildWaterfallAllocationsAsync(propertyId, payment.Id, request.AmountPaid, cancellationToken);
        _dbContext.PaymentAllocations.AddRange(allocations);
        payment.Allocations = allocations;
        // US-37: whatever the waterfall couldn't apply to any charge becomes this payment's
        // own retained credit balance, drawn down later via CreditAllocation or paid out via
        // RefundTransaction -- see PaymentTransaction.UnallocatedAmount's own comment.
        payment.UnallocatedAmount = request.AmountPaid - allocations.Sum(a => a.AllocatedAmount);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var chargeDescriptionsById = await GetChargeDescriptionsAsync(allocations.Select(a => a.ChargeId), cancellationToken);
        var residentName = $"{resident.FirstName} {resident.LastName}";
        return CreatedAtAction(
            nameof(GetPayment), new { propertyId, id = payment.Id }, ToResponse(payment, residentName, chargeDescriptionsById));
    }

    /// <summary>
    /// US-38: "Reverse Payment" -- an NSF/bounced payment. Un-links this payment's
    /// PaymentAllocation rows (restoring the charges they were applied to back toward Unpaid,
    /// since those charges' AllocatedAmount is always computed live from the surviving rows --
    /// see ChargesController's own comment) and any CreditAllocation rows sourced from it
    /// (the money it was "holding as credit" never really existed either), then zeroes its own
    /// UnallocatedAmount. The row itself is never deleted -- see PaymentTransaction's own
    /// class comment on why.
    /// </summary>
    [HttpPost("{id:guid}/reverse")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> ReversePayment(
        Guid propertyId, Guid id, [FromBody] ReversePaymentRequest request, CancellationToken cancellationToken)
    {
        var payment = await _dbContext.PaymentTransactions
            .Include(p => p.Allocations)
            .FirstOrDefaultAsync(p => p.PropertyId == propertyId && p.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Payment '{id}' was not found on this property.");

        if (payment.Status == PaymentTransactionStatus.Reversed)
        {
            throw new ConflictException("This payment has already been reversed.");
        }

        var reason = ValidateAndSanitizeReason(request.ReversalReason);

        await ReverseAllocationsAsync(payment.Id, cancellationToken);
        payment.Status = PaymentTransactionStatus.Reversed;
        payment.ReversalReason = reason;
        payment.UnallocatedAmount = 0m;
        payment.Allocations.Clear();

        await _dbContext.SaveChangesAsync(cancellationToken);

        var residentName = await GetResidentNameAsync(payment.ResidentProfileId, cancellationToken);
        return Ok(ToResponse(payment, residentName, []));
    }

    /// <summary>
    /// US-38: "Reallocate Payment" -- a cross-property posting error, not an NSF reversal (the
    /// money is real, it just landed on the wrong door). Reverses this payment exactly like
    /// ReversePayment above, then atomically creates a brand-new PaymentTransaction under the
    /// correct property/resident and runs the statutory waterfall against it, same as a fresh
    /// LogPayment. Both rows end up cross-referencing each other: the original via
    /// ReallocatedToId + ReversalReason, the new one via its own Notes.
    /// </summary>
    [HttpPost("{id:guid}/reallocate")]
    [Authorize(Policy = Permissions.Ledger.Write)]
    public async Task<IActionResult> ReallocatePayment(
        Guid propertyId, Guid id, [FromBody] ReallocatePaymentRequest request, CancellationToken cancellationToken)
    {
        var payment = await _dbContext.PaymentTransactions
            .Include(p => p.Allocations)
            .FirstOrDefaultAsync(p => p.PropertyId == propertyId && p.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Payment '{id}' was not found on this property.");

        if (payment.Status == PaymentTransactionStatus.Reversed)
        {
            throw new ConflictException("This payment has already been reversed.");
        }

        var reason = ValidateAndSanitizeReason(request.ReversalReason);

        if (request.TargetPropertyId == propertyId)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.TargetPropertyId)] = ["Reallocation target must be a different property than the one this payment is currently posted to."],
            });
        }

        await EnsurePropertyExistsAsync(request.TargetPropertyId, cancellationToken);
        var targetResident = await _dbContext.ResidentProfiles
            .FirstOrDefaultAsync(r => r.PropertyId == request.TargetPropertyId && r.Id == request.TargetResidentProfileId, cancellationToken)
            ?? throw new NotFoundException($"Resident '{request.TargetResidentProfileId}' was not found on the target property.");

        await ReverseAllocationsAsync(payment.Id, cancellationToken);

        var newPayment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            PropertyId = request.TargetPropertyId,
            ResidentProfileId = targetResident.Id,
            PaymentDate = payment.PaymentDate,
            AmountPaid = payment.AmountPaid,
            TenderType = payment.TenderType,
            ReferenceNumber = payment.ReferenceNumber,
            Notes = $"Reallocated from payment {payment.Id} originally posted to property {propertyId}. {reason}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.PaymentTransactions.Add(newPayment);

        var newAllocations = await BuildWaterfallAllocationsAsync(request.TargetPropertyId, newPayment.Id, payment.AmountPaid, cancellationToken);
        _dbContext.PaymentAllocations.AddRange(newAllocations);
        newPayment.Allocations = newAllocations;
        newPayment.UnallocatedAmount = payment.AmountPaid - newAllocations.Sum(a => a.AllocatedAmount);

        payment.Status = PaymentTransactionStatus.Reversed;
        payment.ReallocatedToId = newPayment.Id;
        payment.ReversalReason = $"Reallocated to property {request.TargetPropertyId} as payment {newPayment.Id}. {reason}";
        payment.UnallocatedAmount = 0m;
        payment.Allocations.Clear();

        await _dbContext.SaveChangesAsync(cancellationToken);

        var chargeDescriptionsById = await GetChargeDescriptionsAsync(newAllocations.Select(a => a.ChargeId), cancellationToken);
        var residentName = $"{targetResident.FirstName} {targetResident.LastName}";
        return CreatedAtAction(
            nameof(GetPayment), new { propertyId = request.TargetPropertyId, id = newPayment.Id },
            ToResponse(newPayment, residentName, chargeDescriptionsById));
    }

    /// <summary>Un-links (deletes) every PaymentAllocation this payment produced and every
    /// CreditAllocation later drawn FROM its retained credit -- both count toward a charge's
    /// AllocatedAmount identically (see ChargesController's own comment), so removing both
    /// naturally restores every affected charge's computed PaymentStatus without touching the
    /// Charge rows themselves.</summary>
    private async Task ReverseAllocationsAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var paymentAllocations = await _dbContext.PaymentAllocations
            .Where(a => a.PaymentTransactionId == paymentId)
            .ToListAsync(cancellationToken);
        _dbContext.PaymentAllocations.RemoveRange(paymentAllocations);

        var creditAllocations = await _dbContext.CreditAllocations
            .Where(a => a.SourcePaymentTransactionId == paymentId)
            .ToListAsync(cancellationToken);
        _dbContext.CreditAllocations.RemoveRange(creditAllocations);
    }

    private string ValidateAndSanitizeReason(string reason)
    {
        var sanitized = _sanitizer.Sanitize(reason)!;
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            errors["ReversalReason"] = ["Reversal reason is required."];
        }
        else if (sanitized.Length > 250)
        {
            errors["ReversalReason"] = ["Reversal reason must be 250 characters or fewer."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return sanitized;
    }

    /// <summary>
    /// The waterfall itself: loads every Active charge on the unit, works out each one's
    /// current outstanding balance (Amount + net adjustments - amount already allocated), and
    /// walks them in priority order applying as much of the payment as each charge still owes
    /// until either the payment is exhausted or every charge is satisfied. Voided charges are
    /// excluded (nothing is owed on them), same as the read-side balance calc in
    /// ChargesController.GetStatement.
    /// </summary>
    private async Task<List<PaymentAllocation>> BuildWaterfallAllocationsAsync(
        Guid propertyId, Guid paymentTransactionId, decimal amountToAllocate, CancellationToken cancellationToken)
    {
        var activeCharges = await _dbContext.Charges
            .Where(c => c.PropertyId == propertyId && c.Status == ChargeLifecycleStatus.Active)
            .ToListAsync(cancellationToken);
        var chargeIds = activeCharges.Select(c => c.Id).ToList();

        var existingAllocations = await _dbContext.PaymentAllocations
            .Where(a => chargeIds.Contains(a.ChargeId))
            .ToListAsync(cancellationToken);
        var existingAdjustments = await _dbContext.ChargeAdjustments
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken);

        var orderedCharges = activeCharges
            .OrderBy(c => c.AllocationPriority)
            .ThenBy(c => c.DueDate)
            .ToList();

        var newAllocations = new List<PaymentAllocation>();
        var remaining = amountToAllocate;

        foreach (var charge in orderedCharges)
        {
            if (remaining <= 0)
            {
                break;
            }

            var alreadyAllocated = existingAllocations.Where(a => a.ChargeId == charge.Id).Sum(a => a.AllocatedAmount);
            var netAdjustment = existingAdjustments.Where(a => a.TargetChargeId == charge.Id)
                .Sum(a => a.AdjustmentType == AdjustmentType.DebitAdjustment ? a.Amount : -a.Amount);
            var outstanding = Math.Max(0m, charge.Amount + netAdjustment - alreadyAllocated);

            if (outstanding <= 0)
            {
                continue;
            }

            var amountToApply = Math.Min(remaining, outstanding);
            newAllocations.Add(new PaymentAllocation
            {
                Id = Guid.NewGuid(),
                PaymentTransactionId = paymentTransactionId,
                ChargeId = charge.Id,
                AllocatedAmount = amountToApply,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            remaining -= amountToApply;
        }

        return newAllocations;
    }

    private async Task EnsurePropertyExistsAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Properties.AnyAsync(p => p.Id == propertyId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException($"Property '{propertyId}' was not found.");
        }
    }

    private async Task<Dictionary<Guid, string>> GetChargeDescriptionsAsync(IEnumerable<Guid> chargeIds, CancellationToken cancellationToken)
    {
        var ids = chargeIds.Distinct().ToList();
        return await _dbContext.Charges
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Description, cancellationToken);
    }

    private async Task<string> GetResidentNameAsync(Guid residentProfileId, CancellationToken cancellationToken)
    {
        var resident = await _dbContext.ResidentProfiles
            .Where(r => r.Id == residentProfileId)
            .Select(r => new { r.FirstName, r.LastName })
            .FirstOrDefaultAsync(cancellationToken);
        return resident is null ? "(unknown resident)" : $"{resident.FirstName} {resident.LastName}";
    }

    private static PaymentTransactionResponse ToResponse(
        PaymentTransaction payment, string residentName, Dictionary<Guid, string> chargeDescriptionsById) => new(
        payment.Id,
        payment.PropertyId,
        payment.ResidentProfileId,
        residentName,
        payment.PaymentDate,
        payment.AmountPaid,
        payment.TenderType,
        payment.ReferenceNumber,
        payment.Notes,
        payment.UnallocatedAmount,
        payment.Status,
        payment.ReversalReason,
        payment.ReallocatedToId,
        payment.Allocations.Select(a => new PaymentAllocationSummaryResponse(
            a.ChargeId,
            chargeDescriptionsById.GetValueOrDefault(a.ChargeId, "(unknown charge)"),
            a.AllocatedAmount)).ToList());

    private sealed record SanitizedFields(string? ReferenceNumber, string? Notes);

    private SanitizedFields ValidateAndSanitize(LogPaymentRequest request)
    {
        var referenceNumber = NullIfBlank(_sanitizer.Sanitize(request.ReferenceNumber));
        var notes = NullIfBlank(_sanitizer.Sanitize(request.Notes));

        var errors = new Dictionary<string, string[]>();

        if (request.AmountPaid <= 0)
        {
            errors[nameof(request.AmountPaid)] = ["Amount paid must be greater than zero."];
        }

        if (referenceNumber is { Length: > 100 })
        {
            errors[nameof(request.ReferenceNumber)] = ["Reference number must be 100 characters or fewer."];
        }

        if (notes is { Length: > 500 })
        {
            errors[nameof(request.Notes)] = ["Notes must be 500 characters or fewer."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return new SanitizedFields(referenceNumber, notes);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
