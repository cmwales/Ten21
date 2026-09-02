using Microsoft.EntityFrameworkCore;
using Ten21.Domain.Enums;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Billing;

/// <summary>
/// US-47 (Sprint 9): a pure read-time forecast -- evaluates RecurrenceSchedule.IsDueOn for
/// every day in [Today, Today + 30] against a property's active templates, with zero
/// writes and zero impact on the real ledger (BillingCycleService.GenerateRecurringChargesAsync
/// is the only thing that ever posts an actual Charge). Reflects template
/// creation/edits/pause toggles immediately, by construction -- there is nothing cached to
/// go stale.
///
/// Deliberately Property-scoped, not tenant-wide -- "upcoming dues" is meaningful per unit,
/// the same way a statement is. Permissions.Ledger.Read-gated at the controller, same as
/// every other ledger read; CLAUDE.md's hard-block on non-owner Tenant access to financial
/// ledgers applies here too (RolePermissions' Tenant bundle has no Ledger.Read grant today),
/// so this ships Property-Manager-only for now despite US-47's "As a Resident..." framing --
/// extending it to residents would be a deliberate, separate permission decision, not
/// something to fold in silently here.
/// </summary>
public class DuesProjectionService
{
    private const int WindowDays = 30;

    private readonly Ten21DbContext _dbContext;

    public DuesProjectionService(Ten21DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<UpcomingDueResponse>> GetProjectionAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        await _dbContext.EnsurePropertyExistsAsync(propertyId, cancellationToken);

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        var windowEnd = today.AddDays(WindowDays);

        var templates = await _dbContext.LeaseRecurringCharges
            .Where(t => !t.IsPaused && t.EffectiveStartDate <= windowEnd)
            .Join(_dbContext.Leases.Where(l => l.PropertyId == propertyId), t => t.LeaseId, l => l.Id, (t, l) => new { Template = t, l.EndDate })
            .ToListAsync(cancellationToken);

        var projections = new List<UpcomingDueResponse>();
        foreach (var candidate in templates)
        {
            var template = candidate.Template;
            var effectiveEndDate = template.EndStrategy switch
            {
                EndStrategy.Indefinite => (DateOnly?)null,
                EndStrategy.FixedDate => template.EffectiveEndDate,
                EndStrategy.LeaseAligned => candidate.EndDate,
                _ => throw new ArgumentOutOfRangeException(nameof(template.EndStrategy)),
            };

            for (var date = today; date <= windowEnd; date = date.AddDays(1))
            {
                if (effectiveEndDate is { } end && date > end)
                {
                    break;
                }

                if (RecurrenceSchedule.IsDueOn(template, date))
                {
                    projections.Add(new UpcomingDueResponse(template.Id, template.ChargeName, template.Category, template.Amount, date));
                }
            }
        }

        return projections.OrderBy(p => p.DueDate).ToList();
    }
}

public record UpcomingDueResponse(Guid TemplateId, string ChargeName, ChargeCategory Category, decimal Amount, DateOnly DueDate);
