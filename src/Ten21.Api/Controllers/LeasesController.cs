using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Leases;
using Ten21.Api.Contracts.ManualCharges;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-30: attaches a ResidentProfile to a Property with contract dates, base rent, a
/// recurring billing anchor day, and optional recurring sub-charges -- the prerequisite data
/// this Sprint establishes for Sprint 7's automated recurring billing. Nested resource
/// (api/properties/{propertyId}/leases), same BOLA/IDOR-safe convention as ResidentsController:
/// every action re-checks PropertyId == the route's propertyId rather than trusting a bare
/// {id} lookup.
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/leases")]
public class LeasesController : ControllerBase
{
    /// <summary>US-32: no threshold is named in the acceptance criteria ("Displays a visual
    /// 'Lease Expiring Soon' status badge... when approaching EndDate") -- 60 days is a
    /// standard leasing-industry renewal-notice window, chosen as a sensible default rather
    /// than left unspecified.</summary>
    private const int ExpiringSoonThresholdDays = 60;

    private readonly Ten21DbContext _dbContext;
    private readonly IInputSanitizer _sanitizer;

    public LeasesController(Ten21DbContext dbContext, IInputSanitizer sanitizer)
    {
        _dbContext = dbContext;
        _sanitizer = sanitizer;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Lease.Read)]
    public async Task<IActionResult> GetLeases(Guid propertyId, CancellationToken cancellationToken)
    {
        await EnsurePropertyExistsAsync(propertyId, cancellationToken);

        var leases = await _dbContext.Leases
            .Include(l => l.RecurringCharges)
            .Where(l => l.PropertyId == propertyId)
            .OrderByDescending(l => l.StartDate)
            .ToListAsync(cancellationToken);

        return Ok(leases.Select(l => ToResponse(l, l.RecurringCharges.ToList())).ToList());
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Lease.Read)]
    public async Task<IActionResult> GetLease(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var lease = await FindLeaseAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Lease '{id}' was not found on this property.");

        return Ok(ToResponse(lease, lease.RecurringCharges.ToList()));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Lease.Manage)]
    public async Task<IActionResult> CreateLease(
        Guid propertyId, [FromBody] UpsertLeaseRequest request, CancellationToken cancellationToken)
    {
        await EnsurePropertyExistsAsync(propertyId, cancellationToken);
        await EnsureResidentBelongsToPropertyAsync(propertyId, request.ResidentId, cancellationToken);
        ValidateRequest(request);

        var lease = new Lease
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            ResidentId = request.ResidentId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            MonthlyBaseRent = request.MonthlyBaseRent,
            DueDayOfMonth = request.DueDayOfMonth,
            Status = request.Status,
            MoveOutNoticeDate = request.MoveOutNoticeDate,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _dbContext.Leases.Add(lease);
        var charges = BuildRecurringCharges(lease.Id, request.RecurringCharges);
        _dbContext.LeaseRecurringCharges.AddRange(charges);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetLease), new { propertyId, id = lease.Id }, ToResponse(lease, charges));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Lease.Manage)]
    public async Task<IActionResult> UpdateLease(
        Guid propertyId, Guid id, [FromBody] UpsertLeaseRequest request, CancellationToken cancellationToken)
    {
        await EnsureResidentBelongsToPropertyAsync(propertyId, request.ResidentId, cancellationToken);
        ValidateRequest(request);

        var lease = await FindLeaseAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Lease '{id}' was not found on this property.");

        lease.ResidentId = request.ResidentId;
        lease.StartDate = request.StartDate;
        lease.EndDate = request.EndDate;
        lease.MonthlyBaseRent = request.MonthlyBaseRent;
        lease.DueDayOfMonth = request.DueDayOfMonth;
        lease.Status = request.Status;
        lease.MoveOutNoticeDate = request.MoveOutNoticeDate;

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

        return Ok(ToResponse(lease, newCharges));
    }

    /// <summary>
    /// US-32: "Create Move-In Charge" -- generates a one-time ManualCharge covering the
    /// partial period from MoveInDate through the day before the lease's next regular
    /// DueDayOfMonth billing cycle. Deliberately reuses ManualCharge (US-31) rather than a
    /// separate ProRatedCharge table -- the two are functionally identical open ledger line
    /// items (unit + optional resident + description + amount + due date + GL code); the
    /// only thing distinguishing a "pro-rated" charge is that ITS amount is computed instead
    /// of typed in, which doesn't need its own storage shape.
    /// </summary>
    [HttpPost("{id:guid}/move-in-charge")]
    [Authorize(Policy = Permissions.Lease.Manage)]
    public async Task<IActionResult> CreateMoveInCharge(
        Guid propertyId, Guid id, [FromBody] CreateMoveInChargeRequest request, CancellationToken cancellationToken)
    {
        var lease = await FindLeaseAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Lease '{id}' was not found on this property.");

        if (request.MoveInDate < lease.StartDate || request.MoveInDate >= lease.EndDate)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.MoveInDate)] = ["Move-in date must fall within the lease's start and end dates."],
            });
        }

        var (description, amount) = ComputeProration(lease, request.MoveInDate);

        var charge = new ManualCharge
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            ResidentId = lease.ResidentId,
            Description = description,
            Amount = amount,
            DueDate = request.MoveInDate,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _dbContext.ManualCharges.Add(charge);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new ManualChargeResponse(
            charge.Id, charge.PropertyId, charge.ResidentId, charge.Description, charge.Amount, charge.DueDate, charge.AccountingCode));
    }

    /// <summary>
    /// The daily rate is MonthlyBaseRent / days-in-the-move-in-month -- a common, simple
    /// landlord convention. The billed PERIOD itself can still cross into the next calendar
    /// month (e.g. a due day of 5 with an Aug 25 move-in bills through Sep 4), matching the
    /// acceptance criteria's "prior to standard monthly billing anchor start" wording rather
    /// than a plain calendar-month cutoff.
    /// </summary>
    private static (string Description, decimal Amount) ComputeProration(Lease lease, DateOnly moveInDate)
    {
        var billingStart = moveInDate.Day < lease.DueDayOfMonth
            ? new DateOnly(moveInDate.Year, moveInDate.Month, lease.DueDayOfMonth)
            : new DateOnly(moveInDate.Year, moveInDate.Month, lease.DueDayOfMonth).AddMonths(1);

        var daysInPeriod = billingStart.DayNumber - moveInDate.DayNumber;
        var daysInMoveInMonth = DateTime.DaysInMonth(moveInDate.Year, moveInDate.Month);
        var dailyRate = lease.MonthlyBaseRent / daysInMoveInMonth;
        var amount = Math.Round(dailyRate * daysInPeriod, 2);

        var periodEnd = billingStart.AddDays(-1);
        var description = $"Pro-Rated Rent: {moveInDate:MMM d} - {periodEnd:MMM d}";

        return (description, amount);
    }

    /// <summary>Always a soft delete (Lease is ISoftDelete, and AuditSaveChangesInterceptor
    /// converts the Remove() below automatically) -- a lease is a contract record, never
    /// hard-erased.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Lease.Manage)]
    public async Task<IActionResult> DeleteLease(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var lease = await FindLeaseAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Lease '{id}' was not found on this property.");

        _dbContext.Leases.Remove(lease);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task EnsurePropertyExistsAsync(Guid propertyId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Properties.AnyAsync(p => p.Id == propertyId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException($"Property '{propertyId}' was not found.");
        }
    }

    /// <summary>A resident is only ever attached to a lease on the SAME property their
    /// ResidentProfile already belongs to -- that's how they came to exist under this
    /// property in the first place (ResidentsController.CreateResident). Guards against a PM
    /// mistakenly (or maliciously) leasing a unit to a resident profile that actually lives
    /// under a different property.</summary>
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

    private async Task<Lease?> FindLeaseAsync(Guid propertyId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.Leases
            .Include(l => l.RecurringCharges)
            .FirstOrDefaultAsync(l => l.PropertyId == propertyId && l.Id == id, cancellationToken);

    private List<LeaseRecurringCharge> BuildRecurringCharges(
        Guid leaseId, IReadOnlyList<LeaseRecurringChargeRequest> charges) =>
        charges.Select(c => new LeaseRecurringCharge
        {
            Id = Guid.NewGuid(),
            LeaseId = leaseId,
            ChargeName = _sanitizer.Sanitize(c.ChargeName)!,
            Amount = c.Amount,
            AccountingCode = NullIfBlank(_sanitizer.Sanitize(c.AccountingCode)),
            CreatedAt = DateTimeOffset.UtcNow,
        }).ToList();

    private static void ValidateRequest(UpsertLeaseRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.EndDate <= request.StartDate)
        {
            errors[nameof(request.EndDate)] = ["End date must be after the start date."];
        }

        if (request.MonthlyBaseRent < 0)
        {
            errors[nameof(request.MonthlyBaseRent)] = ["Monthly base rent cannot be negative."];
        }

        if (request.DueDayOfMonth is < 1 or > 28)
        {
            errors[nameof(request.DueDayOfMonth)] = ["Due day of month must be between 1 and 28."];
        }

        for (var i = 0; i < request.RecurringCharges.Count; i++)
        {
            var charge = request.RecurringCharges[i];
            if (string.IsNullOrWhiteSpace(charge.ChargeName))
            {
                errors[$"RecurringCharges[{i}].ChargeName"] = ["Charge name is required."];
            }
            else if (charge.ChargeName.Length > 100)
            {
                errors[$"RecurringCharges[{i}].ChargeName"] = ["Charge name must be 100 characters or fewer."];
            }

            if (charge.Amount < 0)
            {
                errors[$"RecurringCharges[{i}].Amount"] = ["Charge amount cannot be negative."];
            }

            if (charge.AccountingCode is { Length: > 50 })
            {
                errors[$"RecurringCharges[{i}].AccountingCode"] = ["Accounting code must be 50 characters or fewer."];
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
    /// US-32: "automatically transition into continuous month-to-month billing status
    /// without requiring manual contract extension records" -- computed here at read time
    /// rather than a stored transition a background job would need to run (this codebase has
    /// no scheduler yet; Phase 1 is still pending). The stored Status column is left
    /// untouched -- a GET having the side effect of mutating a row would be its own kind of
    /// surprising. The only thing that stops the rollover is a move-out notice being on file,
    /// regardless of whether that notice date has itself passed yet.
    /// </summary>
    private static LeaseStatus ComputeEffectiveStatus(Lease lease, DateOnly today)
    {
        if (lease.Status == LeaseStatus.FixedTerm && today > lease.EndDate && lease.MoveOutNoticeDate is null)
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

    private static LeaseResponse ToResponse(Lease lease, IReadOnlyList<LeaseRecurringCharge> charges)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        var effectiveStatus = ComputeEffectiveStatus(lease, today);

        return new LeaseResponse(
            lease.Id,
            lease.PropertyId,
            lease.ResidentId,
            lease.StartDate,
            lease.EndDate,
            lease.MonthlyBaseRent,
            lease.DueDayOfMonth,
            lease.Status,
            lease.MoveOutNoticeDate,
            lease.MonthlyBaseRent + charges.Sum(c => c.Amount),
            charges.Select(c => new LeaseRecurringChargeResponse(c.Id, c.ChargeName, c.Amount, c.AccountingCode)).ToList(),
            effectiveStatus,
            ComputeIsExpiringSoon(lease, today, effectiveStatus));
    }
}
