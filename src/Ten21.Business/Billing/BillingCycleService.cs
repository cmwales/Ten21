using Microsoft.EntityFrameworkCore;
using Ten21.Application.Abstractions;
using Ten21.Business.Charges;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Billing;

/// <summary>
/// US-44/US-45 (Sprint 9): the recurring-charge + late-fee generation engine. Runs
/// entirely within the current request's ambient tenant (no tenantId/propertyId
/// parameter -- every query is already scoped by the EF global query filter, same as
/// every other Business Service). RunCycleAsync is called directly for a PM's own
/// tenant (BillingController.RunCycle, ambient JWT); RunCycleForTenantAsync wraps it for
/// callers that must name an explicit tenant they don't have a JWT for -- the internal
/// scheduler and a SuperAdmin's manual retry -- see docs/User_Stories_Sprint_9.md.
///
/// The whole cycle for one tenant is one atomic unit -- RunCycleAsync owns its own
/// transaction (the one Business-Service exception to "SaveChangesAsync exactly once,"
/// same as CLAUDE.md's other named multi-save-atomicity case) so a failure partway through
/// rolls back everything generated so far this run, including any late fees, never leaving
/// a partial cycle posted.
/// </summary>
public class BillingCycleService
{
    private readonly Ten21DbContext _dbContext;
    private readonly ChargeRepository _chargeRepository;
    private readonly ITenantContext _tenantContext;

    public BillingCycleService(Ten21DbContext dbContext, ChargeRepository chargeRepository, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _chargeRepository = chargeRepository;
        _tenantContext = tenantContext;
    }

    public async Task<BillingCycleResult> RunCycleAsync(CancellationToken cancellationToken)
    {
        var executionDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var chargesGenerated = await GenerateRecurringChargesAsync(executionDate, cancellationToken);
            var lateFeesAssessed = await AssessLateFeesAsync(executionDate, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new BillingCycleResult(chargesGenerated, lateFeesAssessed);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// US-45: wraps RunCycleAsync for a caller (the internal scheduler, or a SuperAdmin's
    /// manual retry) that must name an explicit tenant it has no JWT for -- TenantMiddleware
    /// only ever trusts signed JWT claims for tenant resolution (see its own doc comment;
    /// this codebase deliberately never trusts a client-supplied tenant header), so both
    /// callers authenticate some other way (internal API key, or a SuperAdmin's own JWT)
    /// and pass tenantId as an explicit, ordinary request parameter instead.
    ///
    /// Writes a BillingCycleRun row either way, success or failure -- deliberately in a
    /// SEPARATE SaveChangesAsync call after RunCycleAsync's own transaction has already
    /// committed or rolled back, so the log entry survives even when everything else this
    /// run attempted gets rolled back. Never rethrows: the result IS the answer for both
    /// callers, not an exception they need to catch.
    /// </summary>
    public async Task<BillingCycleRunResult> RunCycleForTenantAsync(
        Guid tenantId, BillingCycleTrigger triggeredBy, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);

        var run = new BillingCycleRun
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RunDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime),
            TriggeredBy = triggeredBy,
            StartedAt = DateTimeOffset.UtcNow,
        };

        try
        {
            var result = await RunCycleAsync(cancellationToken);
            run.Status = BillingCycleRunStatus.Success;
            run.CompletedAt = DateTimeOffset.UtcNow;
            await SaveRunLogAsync(run, cancellationToken);
            return new BillingCycleRunResult(run.Id, BillingCycleRunStatus.Success, null, result);
        }
        catch (Exception ex)
        {
            run.Status = BillingCycleRunStatus.Failed;
            run.ErrorMessage = ex.Message;
            run.CompletedAt = DateTimeOffset.UtcNow;
            await SaveRunLogAsync(run, cancellationToken);
            return new BillingCycleRunResult(run.Id, BillingCycleRunStatus.Failed, ex.Message, null);
        }
    }

    /// <summary>Isolated so a failure writing the log itself can never be mistaken for the
    /// billing cycle's own outcome, and so this SaveChangesAsync is unambiguously outside
    /// RunCycleAsync's own (by now already resolved) transaction.</summary>
    private async Task SaveRunLogAsync(BillingCycleRun run, CancellationToken cancellationToken)
    {
        _dbContext.BillingCycleRuns.Add(run);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> GenerateRecurringChargesAsync(DateOnly executionDate, CancellationToken cancellationToken)
    {
        // No navigation property between LeaseRecurringCharge and Lease (same "resolve by
        // scalar Id" convention as every other entity in this codebase) -- an explicit join
        // gets us Lease.PropertyId (Charge requires it) and Lease.EndDate (LeaseAligned's
        // dynamic end boundary) without one.
        var candidates = await _dbContext.LeaseRecurringCharges
            .Where(t => !t.IsPaused && t.EffectiveStartDate <= executionDate)
            .Join(_dbContext.Leases, t => t.LeaseId, l => l.Id, (t, l) => new { Template = t, l.PropertyId, l.EndDate })
            .ToListAsync(cancellationToken);

        var generated = 0;
        foreach (var candidate in candidates)
        {
            var template = candidate.Template;
            var effectiveEndDate = template.EndStrategy switch
            {
                EndStrategy.Indefinite => (DateOnly?)null,
                EndStrategy.FixedDate => template.EffectiveEndDate,
                EndStrategy.LeaseAligned => candidate.EndDate,
                _ => throw new ArgumentOutOfRangeException(nameof(template.EndStrategy)),
            };
            if (effectiveEndDate is { } end && end < executionDate)
            {
                continue;
            }

            if (!RecurrenceSchedule.IsDueOn(template, executionDate))
            {
                continue;
            }

            var alreadyPosted = await _dbContext.Charges.AnyAsync(
                c => c.SourceRecurringChargeId == template.Id && c.DueDate == executionDate, cancellationToken);
            if (alreadyPosted)
            {
                continue;
            }

            var isFirstOccurrence = !await _dbContext.Charges.AnyAsync(
                c => c.SourceRecurringChargeId == template.Id, cancellationToken);
            var amount = ResolveAmount(template, executionDate, isFirstOccurrence);
            if (amount is null)
            {
                // ZeroFirstMonth's first occurrence: still post a $0 charge so idempotency
                // and "is this the first occurrence" tracking both see it as handled,
                // instead of re-triggering the same skip every subsequent run.
                amount = 0m;
            }

            _dbContext.Charges.Add(new Charge
            {
                Id = Guid.NewGuid(),
                PropertyId = candidate.PropertyId,
                Description = string.IsNullOrWhiteSpace(template.Description) ? template.ChargeName : template.Description,
                Amount = amount.Value,
                DueDate = executionDate,
                AccountingCode = template.AccountingCode,
                Category = template.Category,
                AllocationPriority = Charge.DefaultAllocationPriorityFor(template.Category),
                SourceRecurringChargeId = template.Id,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            generated++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return generated;
    }

    /// <summary>
    /// US-45: assesses late fees against overdue BaseRent balances. Charges are
    /// Property-scoped, not Lease-scoped (same "billed to the unit, not the person"
    /// convention as everywhere else in this codebase -- see Charge's own class comment),
    /// so assessment operates per Property: one property's overdue BaseRent balance,
    /// governed by whichever Lease's LateFeePolicy is found for that property. A property
    /// with more than one simultaneous lease that each carry their own policy is a narrow,
    /// known edge case this doesn't fully disambiguate -- the first policy found wins.
    ///
    /// Idempotency differs by PolicyType: Flat/Percentage/Hybrid are a one-time penalty for
    /// becoming overdue, keyed to the OLDEST currently-overdue charge's own DueDate (stable
    /// across every re-run while that specific debt stays unpaid, so it's assessed exactly
    /// once per delinquency episode -- once that oldest charge is paid off, the next-oldest
    /// remaining overdue charge becomes the new key, allowing a fresh assessment against a
    /// new delinquency). DailyAccruing is keyed to executionDate instead, since it's meant
    /// to add a fresh increment every day the balance stays overdue.
    /// </summary>
    private async Task<int> AssessLateFeesAsync(DateOnly executionDate, CancellationToken cancellationToken)
    {
        var policiesByProperty = await _dbContext.LateFeePolicies
            .Join(_dbContext.Leases, p => p.LeaseId, l => l.Id, (p, l) => new { Policy = p, l.PropertyId })
            .ToListAsync(cancellationToken);
        var distinctPolicies = policiesByProperty.GroupBy(x => x.PropertyId).Select(g => g.First()).ToList();

        var assessed = 0;
        foreach (var entry in distinctPolicies)
        {
            var propertyId = entry.PropertyId;
            var policy = entry.Policy;

            var overdueCharges = await _dbContext.Charges.AsNoTracking()
                .Where(c => c.PropertyId == propertyId
                    && c.Category == ChargeCategory.BaseRent
                    && c.Status == ChargeLifecycleStatus.Active
                    && c.DueDate.AddDays(policy.GracePeriodDays) < executionDate)
                .OrderBy(c => c.DueDate)
                .ToListAsync(cancellationToken);
            if (overdueCharges.Count == 0)
            {
                continue;
            }

            var chargeIds = overdueCharges.Select(c => c.Id).ToList();
            var allocatedAmounts = await _chargeRepository.GetAllocatedAmountsAsync(chargeIds, cancellationToken);
            var adjustmentsByCharge = await _chargeRepository.ListAdjustmentsByChargeIdsAsync(chargeIds, cancellationToken);

            var stillOverdue = overdueCharges
                .Select(c => new
                {
                    Charge = c,
                    Outstanding = ChargeLedgerMath.Outstanding(
                        c.Amount,
                        ChargeLedgerMath.NetAdjustment(adjustmentsByCharge.GetValueOrDefault(c.Id, [])),
                        allocatedAmounts.GetValueOrDefault(c.Id, 0m)),
                })
                .Where(x => x.Outstanding > 0)
                .ToList();
            if (stillOverdue.Count == 0)
            {
                continue;
            }

            var overdueBalance = stillOverdue.Sum(x => x.Outstanding);
            var oldestOverdueDueDate = stillOverdue.Min(x => x.Charge.DueDate);
            var lateFeeDueDate = policy.PolicyType == LateFeePolicyType.DailyAccruing
                ? executionDate
                : oldestOverdueDueDate;

            var alreadyPosted = await _dbContext.Charges.AnyAsync(
                c => c.PropertyId == propertyId && c.Category == ChargeCategory.LateFee && c.DueDate == lateFeeDueDate,
                cancellationToken);
            if (alreadyPosted)
            {
                continue;
            }

            var existingLateFeeTotal = await _dbContext.Charges.AsNoTracking()
                .Where(c => c.PropertyId == propertyId && c.Category == ChargeCategory.LateFee && c.Status == ChargeLifecycleStatus.Active)
                .SumAsync(c => c.Amount, cancellationToken);

            var proposedFee = LateFeeCalculator.ComputeFee(policy, overdueBalance);
            var fee = LateFeeCalculator.ApplyCap(policy, proposedFee, existingLateFeeTotal);
            if (fee <= 0)
            {
                continue;
            }

            _dbContext.Charges.Add(new Charge
            {
                Id = Guid.NewGuid(),
                PropertyId = propertyId,
                Description = $"Late Fee ({policy.PolicyType})",
                Amount = fee,
                DueDate = lateFeeDueDate,
                Category = ChargeCategory.LateFee,
                AllocationPriority = Charge.DefaultAllocationPriorityFor(ChargeCategory.LateFee),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            assessed++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return assessed;
    }

    /// <summary>ProrationStrategy only ever applies to a template's very first generated
    /// occurrence -- every later occurrence is always the full Amount. Returns null only
    /// for ZeroFirstMonth's skip case (caller posts a $0 charge instead of nothing, to keep
    /// idempotency/first-occurrence tracking correct -- see GenerateRecurringChargesAsync).</summary>
    private static decimal? ResolveAmount(LeaseRecurringCharge template, DateOnly occurrenceDate, bool isFirstOccurrence)
    {
        if (!isFirstOccurrence || template.ProrationStrategy == ProrationStrategy.FullAmount)
        {
            return template.Amount;
        }

        if (template.ProrationStrategy == ProrationStrategy.ZeroFirstMonth)
        {
            return null;
        }

        // ProRateByDays: charge only for the portion of the period actually covered,
        // starting from EffectiveStartDate rather than the period's normal start.
        var daysCovered = occurrenceDate.DayNumber - template.EffectiveStartDate.DayNumber + 1;
        var periodLengthDays = RecurrenceSchedule.PeriodLengthDays(template, occurrenceDate);
        if (daysCovered <= 0 || daysCovered >= periodLengthDays)
        {
            return template.Amount;
        }

        return Math.Round(template.Amount * daysCovered / periodLengthDays, 2);
    }
}

public record BillingCycleResult(int ChargesGenerated, int LateFeesAssessed);

public record BillingCycleRunResult(Guid RunId, BillingCycleRunStatus Status, string? ErrorMessage, BillingCycleResult? Result);
