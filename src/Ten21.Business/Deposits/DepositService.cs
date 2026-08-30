using Microsoft.EntityFrameworkCore;
using Ten21.Application.Abstractions;
using Ten21.Application.Ledger;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Deposits;

/// <summary>Business-layer refactor: extracted from DepositsController. No interface -- same
/// reasoning as ChargeService/PaymentService. Owns Ten21DbContext directly for trivial
/// single-table work and the one SaveChangesAsync call per operation; DepositRepository only
/// for the multi-table batched settlement read.</summary>
public class DepositService
{
    private readonly Ten21DbContext _dbContext;
    private readonly DepositRepository _repository;
    private readonly IInputSanitizer _sanitizer;

    public DepositService(Ten21DbContext dbContext, DepositRepository repository, IInputSanitizer sanitizer)
    {
        _dbContext = dbContext;
        _repository = repository;
        _sanitizer = sanitizer;
    }

    public async Task<IReadOnlyList<SecurityDepositResponse>> GetDepositsAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        await _dbContext.EnsurePropertyExistsAsync(propertyId, cancellationToken);

        var deposits = await _dbContext.SecurityDeposits.AsNoTracking()
            .Where(d => d.PropertyId == propertyId)
            .OrderByDescending(d => d.CollectedDate)
            .ToListAsync(cancellationToken);

        var responses = new List<SecurityDepositResponse>(deposits.Count);
        foreach (var deposit in deposits)
        {
            responses.Add(await BuildResponseAsync(deposit, cancellationToken));
        }

        return responses;
    }

    public Task<SecurityDeposit?> FindAsync(Guid propertyId, Guid id, CancellationToken cancellationToken) =>
        _dbContext.SecurityDeposits.FirstOrDefaultAsync(d => d.PropertyId == propertyId && d.Id == id, cancellationToken);

    public async Task<SecurityDepositResponse> BuildResponseAsync(SecurityDeposit deposit, CancellationToken cancellationToken)
    {
        var residentName = await _dbContext.GetResidentNameAsync(deposit.ResidentProfileId, cancellationToken);
        return new SecurityDepositResponse(
            deposit.Id, deposit.PropertyId, deposit.ResidentProfileId, residentName,
            deposit.OriginalAmount, deposit.AmountHeld, deposit.CollectedDate, deposit.Status);
    }

    /// <summary>Dual-Anchor Attribution: if ResidentProfileId isn't specified, auto-defaults
    /// to the Primary Resident on the unit's active lease (Lease.ResidentId) -- the lease with
    /// the latest StartDate that hasn't ended. Throws a ValidationException if there's no
    /// active lease to default from, rather than silently picking an arbitrary resident.</summary>
    public async Task<SecurityDepositResponse> CollectDepositAsync(
        Guid propertyId, CollectDepositRequest request, CancellationToken cancellationToken)
    {
        await _dbContext.EnsurePropertyExistsAsync(propertyId, cancellationToken);

        if (request.Amount <= 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.Amount)] = ["Amount must be greater than zero."],
            });
        }

        Guid residentProfileId;
        if (request.ResidentProfileId is { } explicitResidentId)
        {
            var explicitResident = await _dbContext.ResidentProfiles
                .FirstOrDefaultAsync(r => r.PropertyId == propertyId && r.Id == explicitResidentId, cancellationToken)
                ?? throw new NotFoundException($"Resident '{explicitResidentId}' was not found on this property.");
            residentProfileId = explicitResident.Id;
        }
        else
        {
            var activeLease = await _dbContext.Leases
                .Where(l => l.PropertyId == propertyId && l.Status != LeaseStatus.Ended)
                .OrderByDescending(l => l.StartDate)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new ValidationException(new Dictionary<string, string[]>
                {
                    [nameof(request.ResidentProfileId)] = ["No active lease on this unit to default a resident from -- select one explicitly."],
                });
            residentProfileId = activeLease.ResidentId;
        }

        var deposit = new SecurityDeposit
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            ResidentProfileId = residentProfileId,
            OriginalAmount = request.Amount,
            AmountHeld = request.Amount,
            CollectedDate = request.CollectedDate,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.SecurityDeposits.Add(deposit);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await BuildResponseAsync(deposit, cancellationToken);
    }

    /// <summary>
    /// The whole point of this story: applies the deposit's entire AmountHeld against the
    /// unit's outstanding Charges in the same statutory priority order as the payment
    /// waterfall, then disburses whatever's left to the resident via a RefundTransaction
    /// (Reason = DepositReturn). If dues exceed AmountHeld, the full deposit is applied and
    /// nothing is refunded -- the unsatisfied remainder simply stays on the unit's normal
    /// Balance/OutstandingAmount figures. One SaveChangesAsync commits the allocations, the
    /// optional refund, and the deposit's own Settled status together.
    /// </summary>
    public async Task<SettleDepositResponse> SettleDepositAsync(
        SecurityDeposit deposit, Guid propertyId, SettleDepositRequest request, CancellationToken cancellationToken)
    {
        if (deposit.Status == SecurityDepositStatus.Settled)
        {
            throw new ConflictException("This security deposit has already been settled.");
        }

        var referenceNumber = NullIfBlank(_sanitizer.Sanitize(request.ReferenceNumber));
        if (referenceNumber is { Length: > 100 })
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.ReferenceNumber)] = ["Reference number must be 100 characters or fewer."],
            });
        }

        var (activeCharges, existingPaymentAllocations, existingCreditAllocations, existingDepositAllocations, existingAdjustments) =
            await _repository.GetSettlementDataAsync(propertyId, cancellationToken);

        var orderedCharges = ChargeLedgerMath.OrderByStatutoryPriority(activeCharges);

        var newAllocations = new List<DepositSettlementAllocation>();
        var remaining = deposit.AmountHeld;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var charge in orderedCharges)
        {
            if (remaining <= 0)
            {
                break;
            }

            var alreadyAllocated = existingPaymentAllocations.Where(a => a.ChargeId == charge.Id).Sum(a => a.AllocatedAmount)
                + existingCreditAllocations.Where(a => a.TargetChargeId == charge.Id).Sum(a => a.AppliedAmount)
                + existingDepositAllocations.Where(a => a.TargetChargeId == charge.Id).Sum(a => a.AppliedAmount)
                + newAllocations.Where(a => a.TargetChargeId == charge.Id).Sum(a => a.AppliedAmount);
            var netAdjustment = ChargeLedgerMath.NetAdjustment(existingAdjustments.Where(a => a.TargetChargeId == charge.Id));
            var outstanding = ChargeLedgerMath.Outstanding(charge.Amount, netAdjustment, alreadyAllocated);

            if (outstanding <= 0)
            {
                continue;
            }

            var amountToApply = Math.Min(remaining, outstanding);
            newAllocations.Add(new DepositSettlementAllocation
            {
                Id = Guid.NewGuid(),
                SecurityDepositId = deposit.Id,
                TargetChargeId = charge.Id,
                AppliedAmount = amountToApply,
                AppliedDate = today,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            remaining -= amountToApply;
        }

        _dbContext.DepositSettlementAllocations.AddRange(newAllocations);

        var amountApplied = newAllocations.Sum(a => a.AppliedAmount);
        var amountRefunded = remaining;

        RefundTransaction? refund = null;
        if (amountRefunded > 0)
        {
            refund = new RefundTransaction
            {
                Id = Guid.NewGuid(),
                ResidentProfileId = deposit.ResidentProfileId,
                PropertyId = propertyId,
                Amount = amountRefunded,
                RefundDate = today,
                TenderType = request.TenderType,
                ReferenceNumber = referenceNumber,
                Reason = RefundReason.DepositReturn,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _dbContext.RefundTransactions.Add(refund);
        }

        deposit.AmountHeld = 0m;
        deposit.Status = SecurityDepositStatus.Settled;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var chargeDescriptionsById = activeCharges.ToDictionary(c => c.Id, c => c.Description);
        var allocationResponses = newAllocations.Select(a => new DepositSettlementAllocationResponse(
            a.Id, a.SecurityDepositId, a.TargetChargeId,
            chargeDescriptionsById.GetValueOrDefault(a.TargetChargeId, "(unknown charge)"),
            a.AppliedAmount, a.AppliedDate)).ToList();

        var residentName = await _dbContext.GetResidentNameAsync(deposit.ResidentProfileId, cancellationToken);
        var refundResponse = refund is null
            ? null
            : new RefundTransactionResponse(
                refund.Id, refund.ResidentProfileId, residentName, refund.PropertyId, refund.Amount,
                refund.RefundDate, refund.TenderType, refund.ReferenceNumber, refund.Reason, refund.CreatedAt);

        return new SettleDepositResponse(
            new SecurityDepositResponse(deposit.Id, deposit.PropertyId, deposit.ResidentProfileId, residentName,
                deposit.OriginalAmount, deposit.AmountHeld, deposit.CollectedDate, deposit.Status),
            amountApplied, amountRefunded, allocationResponses, refundResponse);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
