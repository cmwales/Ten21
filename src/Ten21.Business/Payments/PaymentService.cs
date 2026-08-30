using Microsoft.EntityFrameworkCore;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Payments;

/// <summary>Business-layer refactor: extracted from PaymentsController -- see
/// ChargeService's own comment for the resource-authorization split this depends on (the
/// controller resolves+authorizes via IAuthorizationService.EnsureSameTenantAsync and hands
/// the entity in), and for why this owns Ten21DbContext directly for trivial single-table
/// work and the single SaveChangesAsync call per operation. No interface -- same reasoning as
/// ChargeService.</summary>
public class PaymentService
{
    private readonly Ten21DbContext _dbContext;
    private readonly PaymentRepository _repository;
    private readonly IInputSanitizer _sanitizer;

    public PaymentService(Ten21DbContext dbContext, PaymentRepository repository, IInputSanitizer sanitizer)
    {
        _dbContext = dbContext;
        _repository = repository;
        _sanitizer = sanitizer;
    }

    public Task<PaymentTransaction?> FindAsync(Guid propertyId, Guid id, CancellationToken cancellationToken) =>
        _dbContext.PaymentTransactions
            .Include(p => p.Allocations)
            .FirstOrDefaultAsync(p => p.PropertyId == propertyId && p.Id == id, cancellationToken);

    public async Task<PaymentTransactionResponse> BuildResponseAsync(PaymentTransaction payment, CancellationToken cancellationToken)
    {
        var chargeDescriptionsById = await _repository.GetChargeDescriptionsAsync(
            payment.Allocations.Select(a => a.ChargeId), cancellationToken);
        var residentName = await _dbContext.GetResidentNameAsync(payment.ResidentProfileId, cancellationToken);
        return ToResponse(payment, residentName, chargeDescriptionsById);
    }

    /// <summary>
    /// US-40: assembles the full PDF data model for GetReceipt -- property name/unit plus
    /// everything BuildResponseAsync already knows how to shape, so PaymentsController needs
    /// no direct database access of its own for this endpoint.
    /// </summary>
    public async Task<PaymentReceiptPdfData> BuildReceiptDataAsync(
        PaymentTransaction payment, Guid propertyId, CancellationToken cancellationToken)
    {
        var property = await _dbContext.Properties.AsNoTracking().FirstAsync(p => p.Id == propertyId, cancellationToken);
        var response = await BuildResponseAsync(payment, cancellationToken);

        return new PaymentReceiptPdfData(
            property.Name,
            property.UnitIdentifier,
            response.ResidentName,
            response.PaymentDate,
            response.AmountPaid,
            response.TenderType.ToString(),
            response.ReferenceNumber,
            response.Allocations.Select(a => new PaymentReceiptChargeLine(a.ChargeDescription, a.AllocatedAmount)).ToList());
    }

    /// <summary>
    /// US-34: creates the PaymentTransaction, then immediately allocates the full AmountPaid
    /// across the property's outstanding Charges in statutory priority order (Charge's own
    /// stored AllocationPriority, ascending -- lower number = paid first), oldest DueDate
    /// first within the same priority. Any amount left over once every outstanding charge is
    /// satisfied (an overpayment) is deliberately left unallocated: it still counts toward the
    /// unit's Balance via PaymentTransaction.AmountPaid, it just isn't tied to any one charge.
    /// One SaveChangesAsync commits the payment and its allocations together.
    /// </summary>
    public async Task<PaymentTransactionResponse> LogPaymentAsync(
        Guid propertyId, LogPaymentRequest request, CancellationToken cancellationToken)
    {
        await _dbContext.EnsurePropertyExistsAsync(propertyId, cancellationToken);
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

        var chargeDescriptionsById = await _repository.GetChargeDescriptionsAsync(
            allocations.Select(a => a.ChargeId), cancellationToken);
        var residentName = $"{resident.FirstName} {resident.LastName}";
        return ToResponse(payment, residentName, chargeDescriptionsById);
    }

    /// <summary>
    /// US-38: "Reverse Payment" -- an NSF/bounced payment. Un-links this payment's
    /// PaymentAllocation rows (restoring the charges they were applied to back toward Unpaid,
    /// since those charges' AllocatedAmount is always computed live from the surviving rows)
    /// and any CreditAllocation rows sourced from it (the money it was "holding as credit"
    /// never really existed either), then zeroes its own UnallocatedAmount. The row itself is
    /// never deleted -- see PaymentTransaction's own class comment on why. One
    /// SaveChangesAsync commits the un-link and the status flip together.
    /// </summary>
    public async Task<PaymentTransactionResponse> ReverseAsync(
        PaymentTransaction payment, ReversePaymentRequest request, CancellationToken cancellationToken)
    {
        EnsureNotAlreadyReversed(payment);
        var reason = ValidateAndSanitizeReason(request.ReversalReason);

        await _repository.RemoveAllocationsAsync(payment.Id, cancellationToken);
        payment.Status = PaymentTransactionStatus.Reversed;
        payment.ReversalReason = reason;
        payment.UnallocatedAmount = 0m;
        payment.Allocations.Clear();

        await _dbContext.SaveChangesAsync(cancellationToken);

        var residentName = await _dbContext.GetResidentNameAsync(payment.ResidentProfileId, cancellationToken);
        return ToResponse(payment, residentName, []);
    }

    /// <summary>
    /// US-38: "Reallocate Payment" -- a cross-property posting error, not an NSF reversal (the
    /// money is real, it just landed on the wrong door). Reverses this payment exactly like
    /// ReverseAsync above, then atomically creates a brand-new PaymentTransaction under the
    /// correct property/resident and runs the statutory waterfall against it, same as a fresh
    /// LogPaymentAsync. Both rows end up cross-referencing each other: the original via
    /// ReallocatedToId + ReversalReason, the new one via its own Notes. One SaveChangesAsync
    /// commits the reversal and the new payment together -- either both happen or neither
    /// does.
    /// </summary>
    public async Task<PaymentTransactionResponse> ReallocateAsync(
        PaymentTransaction payment, Guid propertyId, ReallocatePaymentRequest request, CancellationToken cancellationToken)
    {
        EnsureNotAlreadyReversed(payment);
        var reason = ValidateAndSanitizeReason(request.ReversalReason);

        if (request.TargetPropertyId == propertyId)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.TargetPropertyId)] =
                    ["Reallocation target must be a different property than the one this payment is currently posted to."],
            });
        }

        await _dbContext.EnsurePropertyExistsAsync(request.TargetPropertyId, cancellationToken);
        var targetResident = await _dbContext.ResidentProfiles
            .FirstOrDefaultAsync(r => r.PropertyId == request.TargetPropertyId && r.Id == request.TargetResidentProfileId, cancellationToken)
            ?? throw new NotFoundException($"Resident '{request.TargetResidentProfileId}' was not found on the target property.");

        await _repository.RemoveAllocationsAsync(payment.Id, cancellationToken);

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

        var newAllocations = await BuildWaterfallAllocationsAsync(
            request.TargetPropertyId, newPayment.Id, payment.AmountPaid, cancellationToken);
        _dbContext.PaymentAllocations.AddRange(newAllocations);
        newPayment.Allocations = newAllocations;
        newPayment.UnallocatedAmount = payment.AmountPaid - newAllocations.Sum(a => a.AllocatedAmount);

        payment.Status = PaymentTransactionStatus.Reversed;
        payment.ReallocatedToId = newPayment.Id;
        payment.ReversalReason = $"Reallocated to property {request.TargetPropertyId} as payment {newPayment.Id}. {reason}";
        payment.UnallocatedAmount = 0m;
        payment.Allocations.Clear();

        await _dbContext.SaveChangesAsync(cancellationToken);

        var chargeDescriptionsById = await _repository.GetChargeDescriptionsAsync(
            newAllocations.Select(a => a.ChargeId), cancellationToken);
        var residentName = $"{targetResident.FirstName} {targetResident.LastName}";
        return ToResponse(newPayment, residentName, chargeDescriptionsById);
    }

    private static void EnsureNotAlreadyReversed(PaymentTransaction payment)
    {
        if (payment.Status == PaymentTransactionStatus.Reversed)
        {
            throw new ConflictException("This payment has already been reversed.");
        }
    }

    /// <summary>
    /// The waterfall itself: loads every Active charge on the unit, works out each one's
    /// current outstanding balance (Amount + net adjustments - amount already allocated), and
    /// walks them in priority order applying as much of the payment as each charge still owes
    /// until either the payment is exhausted or every charge is satisfied. Voided charges are
    /// excluded (nothing is owed on them).
    /// </summary>
    private async Task<List<PaymentAllocation>> BuildWaterfallAllocationsAsync(
        Guid propertyId, Guid paymentTransactionId, decimal amountToAllocate, CancellationToken cancellationToken)
    {
        var (activeCharges, existingAllocations, existingAdjustments) =
            await _repository.GetWaterfallDataAsync(propertyId, cancellationToken);

        var orderedCharges = ChargeLedgerMath.OrderByStatutoryPriority(activeCharges);

        var newAllocations = new List<PaymentAllocation>();
        var remaining = amountToAllocate;

        foreach (var charge in orderedCharges)
        {
            if (remaining <= 0)
            {
                break;
            }

            var alreadyAllocated = existingAllocations.Where(a => a.ChargeId == charge.Id).Sum(a => a.AllocatedAmount);
            var netAdjustment = ChargeLedgerMath.NetAdjustment(existingAdjustments.Where(a => a.TargetChargeId == charge.Id));
            var outstanding = ChargeLedgerMath.Outstanding(charge.Amount, netAdjustment, alreadyAllocated);

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

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
