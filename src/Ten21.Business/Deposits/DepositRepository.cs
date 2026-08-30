using Microsoft.EntityFrameworkCore;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Deposits;

/// <summary>
/// Data-access rules (see CLAUDE.md): thin, batched-query-only repository -- see
/// ChargeRepository's own comment for the full reasoning.
/// </summary>
public class DepositRepository
{
    private readonly Ten21DbContext _dbContext;

    public DepositRepository(Ten21DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Every Active charge on the unit, plus every existing allocation against those
    /// charges from all three sources that can lock one (payments, credits, prior deposit
    /// settlements) and every adjustment -- four queries SettleDeposit always needs together
    /// to work out what's still outstanding on each charge before applying deposit money.</summary>
    public async Task<(
        List<Charge> ActiveCharges,
        List<PaymentAllocation> ExistingPaymentAllocations,
        List<CreditAllocation> ExistingCreditAllocations,
        List<DepositSettlementAllocation> ExistingDepositAllocations,
        List<ChargeAdjustment> ExistingAdjustments)>
        GetSettlementDataAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        var activeCharges = await _dbContext.Charges
            .Where(c => c.PropertyId == propertyId && c.Status == ChargeLifecycleStatus.Active)
            .ToListAsync(cancellationToken);
        var chargeIds = activeCharges.Select(c => c.Id).ToList();

        var existingPaymentAllocations = await _dbContext.PaymentAllocations
            .Where(a => chargeIds.Contains(a.ChargeId)).ToListAsync(cancellationToken);
        var existingCreditAllocations = await _dbContext.CreditAllocations
            .Where(a => chargeIds.Contains(a.TargetChargeId)).ToListAsync(cancellationToken);
        var existingDepositAllocations = await _dbContext.DepositSettlementAllocations
            .Where(a => chargeIds.Contains(a.TargetChargeId)).ToListAsync(cancellationToken);
        var existingAdjustments = await _dbContext.ChargeAdjustments
            .Where(a => chargeIds.Contains(a.TargetChargeId)).ToListAsync(cancellationToken);

        return (activeCharges, existingPaymentAllocations, existingCreditAllocations, existingDepositAllocations, existingAdjustments);
    }
}
