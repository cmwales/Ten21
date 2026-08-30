using Microsoft.EntityFrameworkCore;
using Ten21.Application.Abstractions;
using Ten21.Application.Ledger;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Refunds;

/// <summary>Business-layer refactor: extracted from RefundsController. No repository -- every
/// query here is a single simple table lookup; the only real logic is the FIFO draw-down
/// algorithm itself, which belongs in the service, not a data-access class. No interface --
/// same reasoning as ChargeService/PaymentService.</summary>
public class RefundService
{
    private readonly Ten21DbContext _dbContext;
    private readonly IInputSanitizer _sanitizer;

    public RefundService(Ten21DbContext dbContext, IInputSanitizer sanitizer)
    {
        _dbContext = dbContext;
        _sanitizer = sanitizer;
    }

    public Task<RefundTransaction?> FindAsync(Guid propertyId, Guid id, CancellationToken cancellationToken) =>
        _dbContext.RefundTransactions.AsNoTracking()
            .FirstOrDefaultAsync(r => r.PropertyId == propertyId && r.Id == id, cancellationToken);

    public async Task<RefundTransactionResponse> BuildResponseAsync(RefundTransaction refund, CancellationToken cancellationToken)
    {
        var residentName = await _dbContext.GetResidentNameAsync(refund.ResidentProfileId, cancellationToken);
        return ToResponse(refund, residentName);
    }

    /// <summary>
    /// Draws down the resident's available credit oldest-payment-first (FIFO) across their
    /// PaymentTransactions on this unit, up to the requested Amount, then records the
    /// disbursement. Rejected outright if the resident doesn't have enough retained credit --
    /// this endpoint only ever pays out money the unit is already holding for them, never
    /// creates new liability. One SaveChangesAsync commits the drawdown and the new refund
    /// record together.
    /// </summary>
    public async Task<RefundTransactionResponse> RefundCreditBalanceAsync(
        Guid propertyId, RefundCreditBalanceRequest request, CancellationToken cancellationToken)
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

        return ToResponse(refund, $"{resident.FirstName} {resident.LastName}");
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
