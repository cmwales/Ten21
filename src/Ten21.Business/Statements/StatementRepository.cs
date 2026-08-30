using Microsoft.EntityFrameworkCore;
using Ten21.Domain.Entities;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Statements;

/// <summary>
/// Business-layer refactor: the data-access piece of the unit statement -- deliberately its
/// own repository rather than folded into ChargeRepository/PaymentRepository, since the
/// statement is a read-heavy aggregation spanning Charges/Payments/Credits/Deposits/Refunds
/// all at once, not a natural fit for any single domain's repository. See ChargeRepository's
/// own comment for why there's no interface here.
/// </summary>
public class StatementRepository
{
    private readonly Ten21DbContext _dbContext;

    public StatementRepository(Ten21DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task EnsurePropertyExistsAsync(Guid propertyId, CancellationToken cancellationToken) =>
        _dbContext.EnsurePropertyExistsAsync(propertyId, cancellationToken);

    /// <summary>Only ever called after EnsurePropertyExistsAsync already confirmed the
    /// property exists, so FirstAsync (not FirstOrDefaultAsync) is safe here.</summary>
    public Task<Property> GetPropertyAsync(Guid propertyId, CancellationToken cancellationToken) =>
        _dbContext.Properties.AsNoTracking().FirstAsync(p => p.Id == propertyId, cancellationToken);

    public Task<List<Charge>> ListChargesAsync(Guid propertyId, CancellationToken cancellationToken) =>
        _dbContext.Charges.AsNoTracking()
            .Where(c => c.PropertyId == propertyId)
            .OrderByDescending(c => c.DueDate)
            .ToListAsync(cancellationToken);

    public Task<List<PaymentAllocation>> ListAllocationsForChargesAsync(
        IReadOnlyCollection<Guid> chargeIds, CancellationToken cancellationToken) =>
        _dbContext.PaymentAllocations.AsNoTracking()
            .Where(a => chargeIds.Contains(a.ChargeId))
            .ToListAsync(cancellationToken);

    public Task<List<ChargeAdjustment>> ListAdjustmentsForChargesAsync(
        IReadOnlyCollection<Guid> chargeIds, CancellationToken cancellationToken) =>
        _dbContext.ChargeAdjustments.AsNoTracking()
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken);

    public Task<List<CreditAllocation>> ListCreditAllocationsForChargesAsync(
        IReadOnlyCollection<Guid> chargeIds, CancellationToken cancellationToken) =>
        _dbContext.CreditAllocations.AsNoTracking()
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken);

    public Task<List<DepositSettlementAllocation>> ListDepositAllocationsForChargesAsync(
        IReadOnlyCollection<Guid> chargeIds, CancellationToken cancellationToken) =>
        _dbContext.DepositSettlementAllocations.AsNoTracking()
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken);

    public Task<List<PaymentTransaction>> ListPaymentsAsync(Guid propertyId, CancellationToken cancellationToken) =>
        _dbContext.PaymentTransactions.AsNoTracking()
            .Include(p => p.Allocations)
            .Where(p => p.PropertyId == propertyId)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync(cancellationToken);

    public Task<List<SecurityDeposit>> ListDepositsAsync(Guid propertyId, CancellationToken cancellationToken) =>
        _dbContext.SecurityDeposits.AsNoTracking()
            .Where(d => d.PropertyId == propertyId)
            .OrderByDescending(d => d.CollectedDate)
            .ToListAsync(cancellationToken);

    public Task<List<RefundTransaction>> ListRefundsAsync(Guid propertyId, CancellationToken cancellationToken) =>
        _dbContext.RefundTransactions.AsNoTracking()
            .Where(r => r.PropertyId == propertyId)
            .OrderByDescending(r => r.RefundDate)
            .ToListAsync(cancellationToken);

    public Task<Dictionary<Guid, string>> GetResidentNamesAsync(IEnumerable<Guid> residentProfileIds, CancellationToken cancellationToken) =>
        _dbContext.GetResidentNamesAsync(residentProfileIds, cancellationToken);
}
