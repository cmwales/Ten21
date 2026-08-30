using Microsoft.EntityFrameworkCore;
using Ten21.Domain.Entities;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Charges;

/// <summary>
/// Data-access rules (see CLAUDE.md): repositories may query the DbContext and stage
/// changes, but never own SaveChangesAsync -- that belongs to the Service, which owns the
/// unit-of-work boundary for the whole business operation. This repository is deliberately
/// thin: it holds only the one query with real batching logic (three grouped queries merged
/// into one dictionary); every trivial single-table find/add/remove that used to live here
/// moved directly onto ChargeService's own Ten21DbContext reference, since a repository
/// method that's just `_dbContext.Charges.Add(charge)` duplicates what DbSet already does
/// with nothing added.
/// </summary>
public class ChargeRepository
{
    private readonly Ten21DbContext _dbContext;

    public ChargeRepository(Ten21DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Sums PaymentAllocation + CreditAllocation + DepositSettlementAllocation for
    /// each requested charge -- all three lock a charge and count toward its
    /// PaymentStatus/OutstandingAmount identically. Batched across every requested charge ID
    /// in 3 queries total, however many charges are asked for (the original N+1 fix this was
    /// extracted from -- see ChargesController's git history for the audit finding).</summary>
    public async Task<Dictionary<Guid, decimal>> GetAllocatedAmountsAsync(
        IReadOnlyCollection<Guid> chargeIds, CancellationToken cancellationToken)
    {
        var fromPayments = await _dbContext.PaymentAllocations.AsNoTracking()
            .Where(a => chargeIds.Contains(a.ChargeId))
            .GroupBy(a => a.ChargeId)
            .Select(g => new { ChargeId = g.Key, Total = g.Sum(a => a.AllocatedAmount) })
            .ToDictionaryAsync(x => x.ChargeId, x => x.Total, cancellationToken);
        var fromCredits = await _dbContext.CreditAllocations.AsNoTracking()
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .GroupBy(a => a.TargetChargeId)
            .Select(g => new { ChargeId = g.Key, Total = g.Sum(a => a.AppliedAmount) })
            .ToDictionaryAsync(x => x.ChargeId, x => x.Total, cancellationToken);
        var fromDeposits = await _dbContext.DepositSettlementAllocations.AsNoTracking()
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .GroupBy(a => a.TargetChargeId)
            .Select(g => new { ChargeId = g.Key, Total = g.Sum(a => a.AppliedAmount) })
            .ToDictionaryAsync(x => x.ChargeId, x => x.Total, cancellationToken);

        return chargeIds.ToDictionary(
            id => id,
            id => fromPayments.GetValueOrDefault(id, 0m) + fromCredits.GetValueOrDefault(id, 0m) + fromDeposits.GetValueOrDefault(id, 0m));
    }

    /// <summary>Adjustments grouped by charge, for the batch GetCharges view -- a genuine
    /// GroupBy/ToDictionary transform, not a bare DbSet passthrough.</summary>
    public async Task<Dictionary<Guid, List<ChargeAdjustment>>> ListAdjustmentsByChargeIdsAsync(
        IReadOnlyCollection<Guid> chargeIds, CancellationToken cancellationToken) =>
        (await _dbContext.ChargeAdjustments.AsNoTracking()
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken))
            .GroupBy(a => a.TargetChargeId)
            .ToDictionary(g => g.Key, g => g.ToList());
}
