using Ten21.Business.Statements;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Credits;

/// <summary>Business-layer refactor: extracted from CreditsController. No interface -- same
/// reasoning as ChargeService/PaymentService. Owns Ten21DbContext directly for the one
/// SaveChangesAsync call; CreditRepository only for the multi-table batched read.</summary>
public class CreditService
{
    private readonly Ten21DbContext _dbContext;
    private readonly CreditRepository _repository;

    public CreditService(Ten21DbContext dbContext, CreditRepository repository)
    {
        _dbContext = dbContext;
        _repository = repository;
    }

    /// <summary>
    /// Loads every payment on this unit with UnallocatedAmount > 0 (oldest first) and every
    /// outstanding Active charge (same statutory priority order as the payment waterfall),
    /// then walks the charges in order, drawing from the oldest available credit source(s)
    /// until either that charge is satisfied or all credit is exhausted. Credit is
    /// unit-scoped, not resident-scoped, same as everything else charges touch -- a charge
    /// has no "which resident" of its own to restrict which resident's credit can pay it, so
    /// any resident's retained credit on this unit can satisfy any of the unit's outstanding
    /// charges. CreditAllocation.SourcePaymentTransactionId still records exactly which
    /// payment (and therefore which resident) supplied each dollar.
    /// </summary>
    public async Task<ApplyCreditsResponse> ApplyCreditsToChargesAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        await _dbContext.EnsurePropertyExistsAsync(propertyId, cancellationToken);

        var (paymentsWithCredit, activeCharges, existingPaymentAllocations, existingCreditAllocations, existingAdjustments) =
            await _repository.GetApplyCreditsDataAsync(propertyId, cancellationToken);

        if (paymentsWithCredit.Count == 0)
        {
            return new ApplyCreditsResponse(0m, []);
        }

        var orderedCharges = ChargeLedgerMath.OrderByStatutoryPriority(activeCharges);

        var newAllocations = new List<CreditAllocation>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var charge in orderedCharges)
        {
            if (paymentsWithCredit.All(p => p.UnallocatedAmount <= 0))
            {
                break;
            }

            var alreadyAllocated = existingPaymentAllocations.Where(a => a.ChargeId == charge.Id).Sum(a => a.AllocatedAmount)
                + existingCreditAllocations.Where(a => a.TargetChargeId == charge.Id).Sum(a => a.AppliedAmount)
                + newAllocations.Where(a => a.TargetChargeId == charge.Id).Sum(a => a.AppliedAmount);
            var netAdjustment = ChargeLedgerMath.NetAdjustment(existingAdjustments.Where(a => a.TargetChargeId == charge.Id));
            var outstanding = ChargeLedgerMath.Outstanding(charge.Amount, netAdjustment, alreadyAllocated);

            while (outstanding > 0)
            {
                var sourcePayment = paymentsWithCredit.FirstOrDefault(p => p.UnallocatedAmount > 0);
                if (sourcePayment is null)
                {
                    break;
                }

                var amountToApply = Math.Min(outstanding, sourcePayment.UnallocatedAmount);
                newAllocations.Add(new CreditAllocation
                {
                    Id = Guid.NewGuid(),
                    SourcePaymentTransactionId = sourcePayment.Id,
                    TargetChargeId = charge.Id,
                    AppliedAmount = amountToApply,
                    AppliedDate = today,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                sourcePayment.UnallocatedAmount -= amountToApply;
                outstanding -= amountToApply;
            }
        }

        _dbContext.CreditAllocations.AddRange(newAllocations);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var chargeDescriptionsById = activeCharges.ToDictionary(c => c.Id, c => c.Description);
        var responses = newAllocations.Select(a => new CreditAllocationResponse(
            a.Id, a.SourcePaymentTransactionId, a.TargetChargeId,
            chargeDescriptionsById.GetValueOrDefault(a.TargetChargeId, "(unknown charge)"),
            a.AppliedAmount, a.AppliedDate)).ToList();

        return new ApplyCreditsResponse(responses.Sum(r => r.AppliedAmount), responses);
    }
}
