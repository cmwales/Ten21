using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Residents;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-23: Tenant Profile Directory. A ResidentProfile always belongs to exactly one
/// Property, addressed here as a nested resource (api/properties/{propertyId}/residents) --
/// every action re-checks `PropertyId == propertyId` in its own query rather than trusting
/// a bare {id} lookup, per CLAUDE.md's BOLA/IDOR resource-based-authorization mandate (the
/// tenant filter alone isn't sufficient: a PM could otherwise probe/mutate a resident under
/// the wrong property by guessing a resident Id).
///
/// Login provisioning (US-24: an ApplicationUser + TenantMembership for any resident with
/// an email) is NOT part of this controller yet -- that's the next branch, layered on top
/// of CreateResident once it exists.
/// </summary>
[ApiController]
[Route("api/properties/{propertyId:guid}/residents")]
public class ResidentsController : ControllerBase
{
    private readonly Ten21DbContext _dbContext;
    private readonly IInputSanitizer _sanitizer;

    public ResidentsController(Ten21DbContext dbContext, IInputSanitizer sanitizer)
    {
        _dbContext = dbContext;
        _sanitizer = sanitizer;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Resident.Read)]
    public async Task<IActionResult> GetResidents(Guid propertyId, CancellationToken cancellationToken)
    {
        await EnsurePropertyExistsAsync(propertyId, cancellationToken);

        var residents = await _dbContext.ResidentProfiles
            .Include(r => r.EmergencyContacts)
            .Where(r => r.PropertyId == propertyId)
            .OrderBy(r => r.OccupantType).ThenBy(r => r.LastName)
            .ToListAsync(cancellationToken);

        return Ok(residents.Select(r => ToResponse(r, r.EmergencyContacts.ToList())).ToList());
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Resident.Read)]
    public async Task<IActionResult> GetResident(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var resident = await FindResidentAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Resident '{id}' was not found on this property.");

        return Ok(ToResponse(resident, resident.EmergencyContacts.ToList()));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Resident.Manage)]
    public async Task<IActionResult> CreateResident(
        Guid propertyId, [FromBody] UpsertResidentRequest request, CancellationToken cancellationToken)
    {
        await EnsurePropertyExistsAsync(propertyId, cancellationToken);
        ValidateRequest(request);

        var resident = new ResidentProfile
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            OccupantType = request.OccupantType,
            FirstName = _sanitizer.Sanitize(request.FirstName)!,
            LastName = _sanitizer.Sanitize(request.LastName)!,
            Email = NullIfBlank(_sanitizer.Sanitize(request.Email)),
            PhoneNumber = NullIfBlank(_sanitizer.Sanitize(request.PhoneNumber)),
            ForwardingAddress = NullIfBlank(_sanitizer.Sanitize(request.ForwardingAddress)),
            NoticeGivenDate = request.NoticeGivenDate,
            ShowInDirectory = request.ShowInDirectory,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var contacts = BuildEmergencyContacts(resident.Id, request.EmergencyContacts);
        foreach (var contact in contacts)
        {
            resident.EmergencyContacts.Add(contact);
        }

        _dbContext.ResidentProfiles.Add(resident);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetResident), new { propertyId, id = resident.Id }, ToResponse(resident, contacts));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Resident.Manage)]
    public async Task<IActionResult> UpdateResident(
        Guid propertyId, Guid id, [FromBody] UpsertResidentRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var resident = await FindResidentAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Resident '{id}' was not found on this property.");

        resident.OccupantType = request.OccupantType;
        resident.FirstName = _sanitizer.Sanitize(request.FirstName)!;
        resident.LastName = _sanitizer.Sanitize(request.LastName)!;
        resident.Email = NullIfBlank(_sanitizer.Sanitize(request.Email));
        resident.PhoneNumber = NullIfBlank(_sanitizer.Sanitize(request.PhoneNumber));
        resident.ForwardingAddress = NullIfBlank(_sanitizer.Sanitize(request.ForwardingAddress));
        resident.NoticeGivenDate = request.NoticeGivenDate;
        resident.ShowInDirectory = request.ShowInDirectory;

        // Managed directly via the DbSet rather than resident.EmergencyContacts navigation
        // mutation (Clear()/Add()) -- a real test run caught that mixing navigation-collection
        // edits on an already-tracked, Include()-loaded parent produces unpredictable entity
        // states here (a freshly re-added contact ended up Modified instead of Added, tripping
        // ApplyTenantStamping's Modified-state ownership check since it had no TenantId yet).
        // Working against the DbSet directly sidesteps that relationship-fixup ambiguity
        // entirely -- explicit Added/Deleted states, no navigation-collection state to reason
        // about.
        var existingContacts = await _dbContext.EmergencyContacts
            .Where(c => c.ResidentProfileId == resident.Id)
            .ToListAsync(cancellationToken);
        _dbContext.EmergencyContacts.RemoveRange(existingContacts);

        var newContacts = BuildEmergencyContacts(resident.Id, request.EmergencyContacts);
        _dbContext.EmergencyContacts.AddRange(newContacts);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(resident, newContacts));
    }

    /// <summary>
    /// Always a soft delete (ResidentProfile is ISoftDelete, and AuditSaveChangesInterceptor
    /// converts the Remove() below automatically) -- unlike Property's US-22 payment-aware
    /// hard/soft branching, nothing here justifies ever hard-deleting occupant history, so
    /// there's no IHardDeleteOverride opt-out call needed.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Resident.Manage)]
    public async Task<IActionResult> DeleteResident(Guid propertyId, Guid id, CancellationToken cancellationToken)
    {
        var resident = await FindResidentAsync(propertyId, id, cancellationToken)
            ?? throw new NotFoundException($"Resident '{id}' was not found on this property.");

        _dbContext.ResidentProfiles.Remove(resident);
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

    private async Task<ResidentProfile?> FindResidentAsync(Guid propertyId, Guid id, CancellationToken cancellationToken) =>
        await _dbContext.ResidentProfiles
            .Include(r => r.EmergencyContacts)
            .FirstOrDefaultAsync(r => r.PropertyId == propertyId && r.Id == id, cancellationToken);

    private List<EmergencyContact> BuildEmergencyContacts(Guid residentId, IReadOnlyList<EmergencyContactRequest> contacts) =>
        contacts.Select(contact => new EmergencyContact
        {
            Id = Guid.NewGuid(),
            ResidentProfileId = residentId,
            Name = _sanitizer.Sanitize(contact.Name)!,
            PhoneNumber = _sanitizer.Sanitize(contact.PhoneNumber)!,
            Relationship = NullIfBlank(_sanitizer.Sanitize(contact.Relationship)),
            CreatedAt = DateTimeOffset.UtcNow,
        }).ToList();

    private static void ValidateRequest(UpsertResidentRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            errors["FirstName"] = ["First name is required."];
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            errors["LastName"] = ["Last name is required."];
        }

        if (request.Email is { Length: > 256 })
        {
            errors["Email"] = ["Email must be 256 characters or fewer."];
        }

        for (var i = 0; i < request.EmergencyContacts.Count; i++)
        {
            var contact = request.EmergencyContacts[i];
            if (string.IsNullOrWhiteSpace(contact.Name))
            {
                errors[$"EmergencyContacts[{i}].Name"] = ["Emergency contact name is required."];
            }

            if (string.IsNullOrWhiteSpace(contact.PhoneNumber))
            {
                errors[$"EmergencyContacts[{i}].PhoneNumber"] = ["Emergency contact phone number is required."];
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static ResidentResponse ToResponse(ResidentProfile resident, IReadOnlyList<EmergencyContact> contacts) => new(
        resident.Id,
        resident.PropertyId,
        resident.UserId,
        resident.OccupantType,
        resident.FirstName,
        resident.LastName,
        resident.Email,
        resident.PhoneNumber,
        resident.ForwardingAddress,
        resident.NoticeGivenDate,
        resident.ShowInDirectory,
        contacts.Select(c => new EmergencyContactResponse(c.Id, c.Name, c.PhoneNumber, c.Relationship)).ToList());
}
