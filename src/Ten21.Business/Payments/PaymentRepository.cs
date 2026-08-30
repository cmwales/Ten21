using Microsoft.EntityFrameworkCore;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Payments;

/// <summary>
/// Business-layer refactor: the data-access piece of the Payments business logic. See
/// ChargeRepository's own comment for why there's no interface here, and why wrapping
/// Ten21DbContext this way isn't a second/parallel data source.
/// </summary>
public class PaymentRepository
{
    private readonly Ten21DbContext _dbContext;

    public PaymentRepository(Ten21DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task EnsurePropertyExistsAsync(Guid propertyId, CancellationToken cancellationToken) =>
        _dbContext.EnsurePropertyExistsAsync(propertyId, cancellationToken);

    public Task<string> GetResidentNameAsync(Guid residentProfileId, CancellationToken cancellationToken) =>
        _dbContext.GetResidentNameAsync(residentProfileId, cancellationToken);

    public Task<ResidentProfile?> FindResidentAsync(Guid propertyId, Guid residentProfileId, CancellationToken cancellationToken) =>
        _dbContext.ResidentProfiles
            .FirstOrDefaultAsync(r => r.PropertyId == propertyId && r.Id == residentProfileId, cancellationToken);

    /// <summary>Only ever called after the caller already resolved+authorized a payment
    /// against this exact propertyId, so the property's existence is already guaranteed --
    /// FirstAsync (not FirstOrDefaultAsync), same as the original controller code this was
    /// extracted from.</summary>
    public Task<Property> GetPropertyAsync(Guid propertyId, CancellationToken cancellationToken) =>
        _dbContext.Properties.AsNoTracking().FirstAsync(p => p.Id == propertyId, cancellationToken);

    public Task<PaymentTransaction?> FindAsync(Guid propertyId, Guid paymentId, CancellationToken cancellationToken) =>
        _dbContext.PaymentTransactions
            .Include(p => p.Allocations)
            .FirstOrDefaultAsync(p => p.PropertyId == propertyId && p.Id == paymentId, cancellationToken);

    public Task<Dictionary<Guid, string>> GetChargeDescriptionsAsync(IEnumerable<Guid> chargeIds, CancellationToken cancellationToken)
    {
        var ids = chargeIds.Distinct().ToList();
        return _dbContext.Charges.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Description, cancellationToken);
    }

    public Task<List<Charge>> ListActiveChargesAsync(Guid propertyId, CancellationToken cancellationToken) =>
        _dbContext.Charges
            .Where(c => c.PropertyId == propertyId && c.Status == ChargeLifecycleStatus.Active)
            .ToListAsync(cancellationToken);

    public Task<List<PaymentAllocation>> ListAllocationsForChargesAsync(IReadOnlyCollection<Guid> chargeIds, CancellationToken cancellationToken) =>
        _dbContext.PaymentAllocations
            .Where(a => chargeIds.Contains(a.ChargeId))
            .ToListAsync(cancellationToken);

    public Task<List<ChargeAdjustment>> ListAdjustmentsForChargesAsync(IReadOnlyCollection<Guid> chargeIds, CancellationToken cancellationToken) =>
        _dbContext.ChargeAdjustments
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken);

    public void Add(PaymentTransaction payment) => _dbContext.PaymentTransactions.Add(payment);

    public void AddAllocations(IEnumerable<PaymentAllocation> allocations) => _dbContext.PaymentAllocations.AddRange(allocations);

    /// <summary>Un-links (deletes) every PaymentAllocation this payment produced and every
    /// CreditAllocation later drawn FROM its retained credit -- both count toward a charge's
    /// AllocatedAmount identically (see ChargeRepository.GetAllocatedAmountsAsync), so removing
    /// both naturally restores every affected charge's computed PaymentStatus without touching
    /// the Charge rows themselves.</summary>
    public async Task RemoveAllocationsAsync(Guid paymentId, CancellationToken cancellationToken)
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

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _dbContext.SaveChangesAsync(cancellationToken);
}
