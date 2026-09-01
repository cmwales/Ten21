using Microsoft.EntityFrameworkCore;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Billing;

/// <summary>
/// US-44 (Sprint 9): the recurring-charge generation engine. Runs entirely within the
/// current request's ambient tenant (no tenantId/propertyId parameter -- every query is
/// already scoped by the EF global query filter, same as every other Business Service),
/// so BillingController.RunCycle can be called once per tenant by an external caller (a PM
/// clicking a button, or the future owner site's scheduler) iterating tenants one at a
/// time. See docs/User_Stories_Sprint_9.md for the full design rationale.
///
/// The whole cycle for one tenant is one atomic unit -- RunCycleAsync owns its own
/// transaction (the one Business-Service exception to "SaveChangesAsync exactly once,"
/// same as CLAUDE.md's other named multi-save-atomicity case) so a failure partway through
/// rolls back everything generated so far this run, never leaving a partial cycle posted.
/// </summary>
public class BillingCycleService
{
    private readonly Ten21DbContext _dbContext;

    public BillingCycleService(Ten21DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BillingCycleResult> RunCycleAsync(CancellationToken cancellationToken)
    {
        var executionDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var chargesGenerated = await GenerateRecurringChargesAsync(executionDate, cancellationToken);

            // US-45 adds late fee assessment here, inside this same transaction -- a failed
            // late fee run rolls back this run's freshly-generated charges too, not just
            // itself, per the "whole cycle is one atomic unit" decision in the sprint doc.

            await transaction.CommitAsync(cancellationToken);
            return new BillingCycleResult(chargesGenerated);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
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

public record BillingCycleResult(int ChargesGenerated);
