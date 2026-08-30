using Microsoft.EntityFrameworkCore;
using Ten21.Domain.Entities;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Charges;

/// <summary>
/// Business-layer refactor: the data-access piece of the Charges business logic, kept as its
/// own class from ChargeService for readability (data access vs. business rules), not because
/// anything else implements or swaps it out -- see this project's own csproj comment for why
/// there's no interface here. Wraps the SAME Ten21DbContext instance ASP.NET Core's DI
/// container already hands to everything else in the request (both this and Ten21DbContext
/// are registered Scoped) -- not a second/parallel data source.
/// </summary>
public class ChargeRepository
{
    private readonly Ten21DbContext _dbContext;

    public ChargeRepository(Ten21DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task EnsurePropertyExistsAsync(Guid propertyId, CancellationToken cancellationToken) =>
        _dbContext.EnsurePropertyExistsAsync(propertyId, cancellationToken);

    public Task<Charge?> FindAsync(Guid propertyId, Guid chargeId, CancellationToken cancellationToken) =>
        _dbContext.Charges.FirstOrDefaultAsync(c => c.PropertyId == propertyId && c.Id == chargeId, cancellationToken);

    public Task<List<Charge>> ListByPropertyAsync(Guid propertyId, CancellationToken cancellationToken) =>
        _dbContext.Charges.AsNoTracking()
            .Where(c => c.PropertyId == propertyId)
            .OrderByDescending(c => c.DueDate)
            .ToListAsync(cancellationToken);

    public Task<List<ChargeAdjustment>> ListAdjustmentsAsync(Guid chargeId, CancellationToken cancellationToken) =>
        _dbContext.ChargeAdjustments.AsNoTracking()
            .Where(a => a.TargetChargeId == chargeId)
            .ToListAsync(cancellationToken);

    public async Task<Dictionary<Guid, List<ChargeAdjustment>>> ListAdjustmentsByChargeIdsAsync(
        IReadOnlyCollection<Guid> chargeIds, CancellationToken cancellationToken) =>
        (await _dbContext.ChargeAdjustments.AsNoTracking()
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken))
            .GroupBy(a => a.TargetChargeId)
            .ToDictionary(g => g.Key, g => g.ToList());

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

    public void Add(Charge charge) => _dbContext.Charges.Add(charge);

    public void Add(ChargeAdjustment adjustment) => _dbContext.ChargeAdjustments.Add(adjustment);

    public void Remove(Charge charge) => _dbContext.Charges.Remove(charge);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _dbContext.SaveChangesAsync(cancellationToken);
}
