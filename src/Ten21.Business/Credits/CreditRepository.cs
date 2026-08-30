using Microsoft.EntityFrameworkCore;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Credits;

/// <summary>
/// Data-access rules (see CLAUDE.md): thin, batched-query-only repository -- see
/// ChargeRepository's own comment for the full reasoning.
/// </summary>
public class CreditRepository
{
    private readonly Ten21DbContext _dbContext;

    public CreditRepository(Ten21DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Every payment on this unit still holding retained credit (oldest first,
    /// FIFO), plus everything the statutory waterfall needs to work out each Active charge's
    /// current outstanding balance before applying that credit -- five queries always fetched
    /// together for this one operation, never independently.</summary>
    public async Task<(
        List<PaymentTransaction> PaymentsWithCredit,
        List<Charge> ActiveCharges,
        List<PaymentAllocation> ExistingPaymentAllocations,
        List<CreditAllocation> ExistingCreditAllocations,
        List<ChargeAdjustment> ExistingAdjustments)>
        GetApplyCreditsDataAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        var paymentsWithCredit = await _dbContext.PaymentTransactions
            .Where(p => p.PropertyId == propertyId && p.UnallocatedAmount > 0)
            .OrderBy(p => p.PaymentDate)
            .ToListAsync(cancellationToken);

        var activeCharges = await _dbContext.Charges
            .Where(c => c.PropertyId == propertyId && c.Status == ChargeLifecycleStatus.Active)
            .ToListAsync(cancellationToken);
        var chargeIds = activeCharges.Select(c => c.Id).ToList();

        var existingPaymentAllocations = await _dbContext.PaymentAllocations
            .Where(a => chargeIds.Contains(a.ChargeId))
            .ToListAsync(cancellationToken);
        var existingCreditAllocations = await _dbContext.CreditAllocations
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken);
        var existingAdjustments = await _dbContext.ChargeAdjustments
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken);

        return (paymentsWithCredit, activeCharges, existingPaymentAllocations, existingCreditAllocations, existingAdjustments);
    }
}
