using Microsoft.EntityFrameworkCore;
using Ten21.Application.Abstractions;
using Ten21.Business.Charges;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Leases;

/// <summary>
/// Business-layer refactor: extracted from LeasesController. No repository -- every query
/// here is a single-table find/list, none of the multi-table batching that justifies one
/// elsewhere (see ChargeRepository's own comment). Depends on ChargeService for
/// CreateMoveInChargeAsync's response shaping, so a pro-rated move-in charge looks identical
/// to any other charge fetched through GetCharge. No interface -- same reasoning as
/// ChargeService/PaymentService.
/// </summary>
public class LeaseService
{
    /// <summary>US-32: no threshold is named in the acceptance criteria ("Displays a visual
    /// 'Lease Expiring Soon' status badge... when approaching EndDate") -- 60 days is a
    /// standard leasing-industry renewal-notice window, chosen as a sensible default rather
    /// than left unspecified.</summary>
    private const int ExpiringSoonThresholdDays = 60;

    private readonly Ten21DbContext _dbContext;
    private readonly ChargeService _chargeService;
    private readonly IInputSanitizer _sanitizer;

    public LeaseService(Ten21DbContext dbContext, ChargeService chargeService, IInputSanitizer sanitizer)
    {
        _dbContext = dbContext;
        _chargeService = chargeService;
        _sanitizer = sanitizer;
    }

    public async Task<IReadOnlyList<LeaseResponse>> GetLeasesAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        var property = await GetPropertyAsync(propertyId, cancellationToken);

        var leases = await _dbContext.Leases.AsNoTracking()
            .Include(l => l.RecurringCharges)
            .Where(l => l.PropertyId == propertyId)
            .OrderByDescending(l => l.StartDate)
            .ToListAsync(cancellationToken);

        return leases.Select(l => ToResponse(l, l.RecurringCharges.ToList(), property.MoveOutNoticeDate)).ToList();
    }

    public Task<Lease?> FindAsync(Guid propertyId, Guid id, CancellationToken cancellationToken) =>
        _dbContext.Leases
            .Include(l => l.RecurringCharges)
            .FirstOrDefaultAsync(l => l.PropertyId == propertyId && l.Id == id, cancellationToken);

    public async Task<LeaseResponse> BuildResponseAsync(Guid propertyId, Lease lease, CancellationToken cancellationToken)
    {
        var property = await GetPropertyAsync(propertyId, cancellationToken);
        return ToResponse(lease, lease.RecurringCharges.ToList(), property.MoveOutNoticeDate);
    }

    public async Task<LeaseResponse> CreateAsync(Guid propertyId, UpsertLeaseRequest request, CancellationToken cancellationToken)
    {
        var property = await GetPropertyAsync(propertyId, cancellationToken);
        await EnsureResidentBelongsToPropertyAsync(propertyId, request.ResidentId, cancellationToken);
        ValidateRequest(request);

        var lease = new Lease
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            ResidentId = request.ResidentId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = request.Status,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _dbContext.Leases.Add(lease);
        var charges = BuildRecurringCharges(lease.Id, request.RecurringCharges);
        _dbContext.LeaseRecurringCharges.AddRange(charges);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(lease, charges, property.MoveOutNoticeDate);
    }

    public async Task<LeaseResponse> UpdateAsync(
        Guid propertyId, Lease lease, UpsertLeaseRequest request, CancellationToken cancellationToken)
    {
        var property = await GetPropertyAsync(propertyId, cancellationToken);
        await EnsureResidentBelongsToPropertyAsync(propertyId, request.ResidentId, cancellationToken);
        ValidateRequest(request);

        lease.ResidentId = request.ResidentId;
        lease.StartDate = request.StartDate;
        lease.EndDate = request.EndDate;
        lease.Status = request.Status;

        // Managed directly via the DbSet, not resident.RecurringCharges navigation mutation --
        // ResidentsController.UpdateResident hit a real bug doing that (a re-added child on an
        // already-tracked, Include()-loaded parent ended up Modified instead of Added, tripping
        // ApplyTenantStamping's Modified-state ownership check before it had a TenantId). Same
        // fix applied here from the start.
        var existingCharges = await _dbContext.LeaseRecurringCharges
            .Where(c => c.LeaseId == lease.Id)
            .ToListAsync(cancellationToken);
        _dbContext.LeaseRecurringCharges.RemoveRange(existingCharges);

        var newCharges = BuildRecurringCharges(lease.Id, request.RecurringCharges);
        _dbContext.LeaseRecurringCharges.AddRange(newCharges);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(lease, newCharges, property.MoveOutNoticeDate);
    }

    /// <summary>
    /// US-32: "Create Move-In Charge" -- generates a one-time Charge (Category=BaseRent)
    /// covering the partial period from MoveInDate through the day before the lease's next
    /// regular DueDayOfMonth billing cycle. Deliberately reuses the general Charge entity
    /// rather than a separate ProRatedCharge table -- the only thing distinguishing a
    /// "pro-rated" charge is that ITS amount is computed instead of typed in, which doesn't
    /// need its own storage shape.
    /// </summary>
    public async Task<ChargeResponse> CreateMoveInChargeAsync(
        Guid propertyId, Lease lease, CreateMoveInChargeRequest request, CancellationToken cancellationToken)
    {
        if (request.MoveInDate < lease.StartDate || request.MoveInDate >= lease.EndDate)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.MoveInDate)] = ["Move-in date must fall within the lease's start and end dates."],
            });
        }

        var baseRentTemplate = lease.RecurringCharges.FirstOrDefault(c => c.Category == ChargeCategory.BaseRent)
            ?? throw new DomainException($"Lease '{lease.Id}' has no Category = BaseRent recurring charge.");
        var (description, amount) = ComputeProration(baseRentTemplate, request.MoveInDate);

        var charge = new Charge
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            Description = description,
            Amount = amount,
            DueDate = request.MoveInDate,
            // A pro-rated move-in charge IS rent -- same waterfall priority as any other
            // BaseRent charge, not a special case.
            Category = ChargeCategory.BaseRent,
            AllocationPriority = Charge.DefaultAllocationPriorityFor(ChargeCategory.BaseRent),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _dbContext.Charges.Add(charge);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return _chargeService.BuildResponse(charge, allocatedAmount: 0m, adjustments: []);
    }

    /// <summary>Always a soft delete (Lease is ISoftDelete, and AuditSaveChangesInterceptor
    /// converts the Remove() below automatically) -- a lease is a contract record, never
    /// hard-erased.</summary>
    public async Task DeleteAsync(Lease lease, CancellationToken cancellationToken)
    {
        _dbContext.Leases.Remove(lease);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Also the source of MoveOutNoticeDate for ToResponse -- fetching the full row
    /// (not just an existence check) costs nothing extra here and every caller needs it.</summary>
    private async Task<Property> GetPropertyAsync(Guid propertyId, CancellationToken cancellationToken) =>
        await _dbContext.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == propertyId, cancellationToken)
            ?? throw new NotFoundException($"Property '{propertyId}' was not found.");

    /// <summary>A resident is only ever attached to a lease on the SAME property their
    /// ResidentProfile already belongs to -- that's how they came to exist under this
    /// property in the first place. Guards against a PM mistakenly (or maliciously) leasing a
    /// unit to a resident profile that actually lives under a different property.</summary>
    private async Task EnsureResidentBelongsToPropertyAsync(
        Guid propertyId, Guid residentId, CancellationToken cancellationToken)
    {
        var belongs = await _dbContext.ResidentProfiles
            .AnyAsync(r => r.Id == residentId && r.PropertyId == propertyId, cancellationToken);
        if (!belongs)
        {
            throw new NotFoundException($"Resident '{residentId}' was not found on this property.");
        }
    }

    private List<LeaseRecurringCharge> BuildRecurringCharges(
        Guid leaseId, IReadOnlyList<LeaseRecurringChargeRequest> charges) =>
        charges.Select(c => new LeaseRecurringCharge
        {
            Id = Guid.NewGuid(),
            LeaseId = leaseId,
            ChargeName = _sanitizer.Sanitize(c.ChargeName)!,
            Category = c.Category,
            Amount = c.Amount,
            AccountingCode = NullIfBlank(_sanitizer.Sanitize(c.AccountingCode)),
            Description = NullIfBlank(_sanitizer.Sanitize(c.Description)),
            RecurrencePattern = c.RecurrencePattern,
            RecurrenceInterval = c.RecurrenceInterval,
            DueDayOfMonth = c.DueDayOfMonth,
            TargetDayOfWeek = c.TargetDayOfWeek,
            SecondaryDueDay = c.SecondaryDueDay,
            EndStrategy = c.EndStrategy,
            EffectiveStartDate = c.EffectiveStartDate,
            EffectiveEndDate = c.EffectiveEndDate,
            ProrationStrategy = c.ProrationStrategy,
            IsPaused = c.IsPaused,
            CreatedAt = DateTimeOffset.UtcNow,
        }).ToList();

    private static void ValidateRequest(UpsertLeaseRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.EndDate <= request.StartDate)
        {
            errors[nameof(request.EndDate)] = ["End date must be after the start date."];
        }

        var baseRentCount = request.RecurringCharges.Count(c => c.Category == ChargeCategory.BaseRent);
        if (baseRentCount != 1)
        {
            errors[nameof(request.RecurringCharges)] =
                ["Exactly one recurring charge with Category = BaseRent is required."];
        }

        for (var i = 0; i < request.RecurringCharges.Count; i++)
        {
            var charge = request.RecurringCharges[i];
            var prefix = $"RecurringCharges[{i}]";

            if (string.IsNullOrWhiteSpace(charge.ChargeName))
            {
                errors[$"{prefix}.ChargeName"] = ["Charge name is required."];
            }
            else if (charge.ChargeName.Length > 100)
            {
                errors[$"{prefix}.ChargeName"] = ["Charge name must be 100 characters or fewer."];
            }

            if (charge.Amount < 0)
            {
                errors[$"{prefix}.Amount"] = ["Charge amount cannot be negative."];
            }

            if (charge.AccountingCode is { Length: > 50 })
            {
                errors[$"{prefix}.AccountingCode"] = ["Accounting code must be 50 characters or fewer."];
            }

            if (charge.Description is { Length: > 200 })
            {
                errors[$"{prefix}.Description"] = ["Description must be 200 characters or fewer."];
            }

            if (charge.EffectiveEndDate is { } end && end < charge.EffectiveStartDate)
            {
                errors[$"{prefix}.EffectiveEndDate"] = ["Effective end date must be on or after the effective start date."];
            }

            switch (charge.RecurrencePattern)
            {
                case RecurrencePattern.Monthly:
                    if (charge.DueDayOfMonth is not (>= 1 and <= 31))
                    {
                        errors[$"{prefix}.DueDayOfMonth"] = ["Due day of month (1-31) is required for a Monthly charge."];
                    }
                    break;
                case RecurrencePattern.SemiMonthly:
                    if (charge.DueDayOfMonth is not (>= 1 and <= 31))
                    {
                        errors[$"{prefix}.DueDayOfMonth"] = ["Due day of month (1-31) is required for a SemiMonthly charge."];
                    }
                    if (charge.SecondaryDueDay is not (>= 1 and <= 31))
                    {
                        errors[$"{prefix}.SecondaryDueDay"] = ["Secondary due day (1-31) is required for a SemiMonthly charge."];
                    }
                    break;
                case RecurrencePattern.Weekly:
                case RecurrencePattern.BiWeekly:
                    if (charge.TargetDayOfWeek is null)
                    {
                        errors[$"{prefix}.TargetDayOfWeek"] = ["Target day of week is required for a Weekly/BiWeekly charge."];
                    }
                    break;
            }

            if (charge.RecurrenceInterval < 1)
            {
                errors[$"{prefix}.RecurrenceInterval"] = ["Recurrence interval must be at least 1."];
            }

            if (charge.EndStrategy == EndStrategy.FixedDate && charge.EffectiveEndDate is null)
            {
                errors[$"{prefix}.EffectiveEndDate"] = ["Effective end date is required when EndStrategy is FixedDate."];
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// The daily rate is the BaseRent template's Amount / days-in-the-move-in-month -- a
    /// common, simple landlord convention. The billed PERIOD itself can still cross into
    /// the next calendar month (e.g. a due day of 5 with an Aug 25 move-in bills through
    /// Sep 4), matching the acceptance criteria's "prior to standard monthly billing
    /// anchor start" wording rather than a plain calendar-month cutoff. Uses the same
    /// runtime day-clamping as RecurrenceSchedule (a due day of 31 in a 30-day month
    /// resolves to the 30th) so this stays consistent with the generation engine.
    /// </summary>
    private static (string Description, decimal Amount) ComputeProration(LeaseRecurringCharge baseRentTemplate, DateOnly moveInDate)
    {
        var dueDay = baseRentTemplate.DueDayOfMonth ?? 1;
        var clampedDueDay = Math.Min(dueDay, DateTime.DaysInMonth(moveInDate.Year, moveInDate.Month));
        var billingStart = moveInDate.Day < clampedDueDay
            ? new DateOnly(moveInDate.Year, moveInDate.Month, clampedDueDay)
            : new DateOnly(moveInDate.Year, moveInDate.Month, clampedDueDay).AddMonths(1);

        var daysInPeriod = billingStart.DayNumber - moveInDate.DayNumber;
        var daysInMoveInMonth = DateTime.DaysInMonth(moveInDate.Year, moveInDate.Month);
        var dailyRate = baseRentTemplate.Amount / daysInMoveInMonth;
        var amount = Math.Round(dailyRate * daysInPeriod, 2);

        var periodEnd = billingStart.AddDays(-1);
        var description = $"Pro-Rated Rent: {moveInDate:MMM d} - {periodEnd:MMM d}";

        return (description, amount);
    }

    /// <summary>
    /// US-32: "automatically transition into continuous month-to-month billing status
    /// without requiring manual contract extension records" -- computed here at read time
    /// rather than a stored transition a background job would need to run (this codebase has
    /// no scheduler yet; Phase 1 is still pending). The stored Status column is left
    /// untouched -- a GET having the side effect of mutating a row would be its own kind of
    /// surprising. The only thing that stops the rollover is the PROPERTY having a move-out
    /// notice on file -- a two-occupant unit where one resident gives notice doesn't mean the
    /// unit itself is vacating, so this is a per-property fact applied uniformly to every
    /// lease on it, not a per-resident one. Regardless of whether that notice date has itself
    /// passed yet.
    /// </summary>
    private static LeaseStatus ComputeEffectiveStatus(Lease lease, DateOnly today, DateOnly? propertyMoveOutNoticeDate)
    {
        if (lease.Status == LeaseStatus.FixedTerm && today > lease.EndDate && propertyMoveOutNoticeDate is null)
        {
            return LeaseStatus.MonthToMonth;
        }

        return lease.Status;
    }

    /// <summary>US-32: only meaningful while the lease is still genuinely FixedTerm and
    /// hasn't already rolled over -- a month-to-month or ended lease isn't "expiring soon" in
    /// the sense this banner means.</summary>
    private static bool ComputeIsExpiringSoon(Lease lease, DateOnly today, LeaseStatus effectiveStatus)
    {
        if (effectiveStatus != LeaseStatus.FixedTerm)
        {
            return false;
        }

        var daysUntilExpiration = lease.EndDate.DayNumber - today.DayNumber;
        return daysUntilExpiration is >= 0 and <= ExpiringSoonThresholdDays;
    }

    private static LeaseResponse ToResponse(
        Lease lease, IReadOnlyList<LeaseRecurringCharge> charges, DateOnly? propertyMoveOutNoticeDate)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        var effectiveStatus = ComputeEffectiveStatus(lease, today, propertyMoveOutNoticeDate);

        // Simplification noted on LeaseResponse itself: sums each currently-active
        // template's per-occurrence Amount as-is, not normalized to a true monthly
        // equivalent for non-Monthly patterns.
        var activeCharges = charges.Where(c => IsCurrentlyActive(c, lease, today)).ToList();

        return new LeaseResponse(
            lease.Id,
            lease.PropertyId,
            lease.ResidentId,
            lease.StartDate,
            lease.EndDate,
            lease.Status,
            activeCharges.Sum(c => c.Amount),
            charges.Select(c => new LeaseRecurringChargeResponse(
                c.Id, c.ChargeName, c.Category, c.Amount, c.RecurrencePattern, c.RecurrenceInterval,
                c.DueDayOfMonth, c.TargetDayOfWeek, c.SecondaryDueDay, c.EndStrategy, c.EffectiveStartDate,
                c.EffectiveEndDate, c.ProrationStrategy, c.IsPaused, c.AccountingCode, c.Description)).ToList(),
            effectiveStatus,
            ComputeIsExpiringSoon(lease, today, effectiveStatus));
    }

    private static bool IsCurrentlyActive(LeaseRecurringCharge charge, Lease lease, DateOnly today)
    {
        if (charge.IsPaused || charge.EffectiveStartDate > today)
        {
            return false;
        }

        var effectiveEndDate = charge.EndStrategy switch
        {
            EndStrategy.Indefinite => (DateOnly?)null,
            EndStrategy.FixedDate => charge.EffectiveEndDate,
            EndStrategy.LeaseAligned => lease.EndDate,
            _ => throw new ArgumentOutOfRangeException(nameof(charge.EndStrategy)),
        };

        return effectiveEndDate is null || effectiveEndDate >= today;
    }
}
