using Microsoft.EntityFrameworkCore;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Payments;

/// <summary>
/// Data-access rules (see CLAUDE.md): repositories may query and stage changes but never own
/// SaveChangesAsync -- see ChargeRepository's own comment for the full reasoning. Kept thin:
/// only the queries that genuinely batch/combine multiple tables. Every trivial single-table
/// find/add lives directly on PaymentService's own Ten21DbContext reference instead.
/// </summary>
public class PaymentRepository
{
    private readonly Ten21DbContext _dbContext;

    public PaymentRepository(Ten21DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Dictionary<Guid, string>> GetChargeDescriptionsAsync(IEnumerable<Guid> chargeIds, CancellationToken cancellationToken)
    {
        var ids = chargeIds.Distinct().ToList();
        return _dbContext.Charges.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Description, cancellationToken);
    }

    /// <summary>The three queries the statutory waterfall needs together: every Active charge
    /// on the unit, plus every existing PaymentAllocation/ChargeAdjustment against those
    /// charges (to work out what's still outstanding on each one before applying new money).
    /// Bundled into one method because they're never fetched independently of each other.</summary>
    public async Task<(List<Charge> ActiveCharges, List<PaymentAllocation> ExistingAllocations, List<ChargeAdjustment> ExistingAdjustments)>
        GetWaterfallDataAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        var activeCharges = await _dbContext.Charges
            .Where(c => c.PropertyId == propertyId && c.Status == ChargeLifecycleStatus.Active)
            .ToListAsync(cancellationToken);
        var chargeIds = activeCharges.Select(c => c.Id).ToList();

        var existingAllocations = await _dbContext.PaymentAllocations
            .Where(a => chargeIds.Contains(a.ChargeId))
            .ToListAsync(cancellationToken);
        var existingAdjustments = await _dbContext.ChargeAdjustments
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .ToListAsync(cancellationToken);

        return (activeCharges, existingAllocations, existingAdjustments);
    }

    /// <summary>Un-links (deletes, via RemoveRange staged for the caller's SaveChangesAsync)
    /// every PaymentAllocation this payment produced and every CreditAllocation later drawn
    /// FROM its retained credit -- both count toward a charge's AllocatedAmount identically,
    /// so removing both naturally restores every affected charge's computed PaymentStatus
    /// without touching the Charge rows themselves. Two related cleanup queries bundled as
    /// one unit, not independently reused elsewhere.</summary>
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
}
