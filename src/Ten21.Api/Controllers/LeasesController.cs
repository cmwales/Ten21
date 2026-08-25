using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Leases;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
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

    private static LeaseResponse ToResponse(Lease lease, IReadOnlyList<LeaseRecurringCharge> charges) => new(
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
        charges.Select(c => new LeaseRecurringChargeResponse(c.Id, c.ChargeName, c.Amount, c.AccountingCode)).ToList());
}
