using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Properties;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-19: the real Property/Unit CRUD surface, replacing the throwaway US-01
/// proof-of-concept this controller used to be (a single unauthenticated GET with no DTO).
/// US-20 will extend the list action with pagination/search; US-21/US-22 add their own
/// actions on their own branches.
/// </summary>
[ApiController]
[Route("api/properties")]
public class PropertiesController : ControllerBase
{
    private readonly Ten21DbContext _dbContext;
    private readonly IInputSanitizer _sanitizer;

    public PropertiesController(Ten21DbContext dbContext, IInputSanitizer sanitizer)
    {
        _dbContext = dbContext;
        _sanitizer = sanitizer;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Property.Read)]
    public async Task<IActionResult> GetProperties(CancellationToken cancellationToken)
    {
        // No manual .Where(p => p.TenantId == ...) anywhere in this method -- the global
        // query filter in Ten21DbContext does it automatically. US-20 replaces this with a
        // lightweight, paginated, searchable projection; this stays a plain full-shape list
        // until that branch lands.
        var properties = await _dbContext.Properties
            .Include(p => p.Units)
            .Select(p => ToResponse(p))
            .ToListAsync(cancellationToken);

        return Ok(properties);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Read)]
    public async Task<IActionResult> GetProperty(Guid id, CancellationToken cancellationToken)
    {
        var property = await _dbContext.Properties
            .Include(p => p.Units)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Property '{id}' was not found.");

        return Ok(ToResponse(property));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> CreateProperty(
        [FromBody] UpsertPropertyRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var property = new Property
        {
            Id = Guid.NewGuid(),
            Name = _sanitizer.Sanitize(request.Name)!,
            PropertyType = request.PropertyType,
            StreetAddress1 = _sanitizer.Sanitize(request.StreetAddress1)!,
            StreetAddress2 = _sanitizer.Sanitize(request.StreetAddress2),
            City = _sanitizer.Sanitize(request.City)!,
            State = _sanitizer.Sanitize(request.State)!,
            PostalCode = _sanitizer.Sanitize(request.PostalCode)!,
            Country = _sanitizer.Sanitize(request.Country)!,
            DefaultTargetRent = request.DefaultTargetRent,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        foreach (var unitRequest in request.Units)
        {
            property.Units.Add(new Domain.Entities.Unit
            {
                Id = Guid.NewGuid(),
                UnitIdentifier = _sanitizer.Sanitize(unitRequest.UnitIdentifier)!,
                // DefaultTargetRent cascades once, at creation time, as a fallback when the
                // caller doesn't override it per-unit -- see the Executive Summary in
                // User_Stories_Sprint_3.md.
                TargetRent = unitRequest.TargetRent ?? request.DefaultTargetRent,
                OccupancyStatus = unitRequest.OccupancyStatus,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        _dbContext.Properties.Add(property);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetProperty), new { id = property.Id }, ToResponse(property));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> UpdateProperty(
        Guid id, [FromBody] UpsertPropertyRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var property = await _dbContext.Properties
            .Include(p => p.Units)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Property '{id}' was not found.");

        property.Name = _sanitizer.Sanitize(request.Name)!;
        property.PropertyType = request.PropertyType;
        property.StreetAddress1 = _sanitizer.Sanitize(request.StreetAddress1)!;
        property.StreetAddress2 = _sanitizer.Sanitize(request.StreetAddress2);
        property.City = _sanitizer.Sanitize(request.City)!;
        property.State = _sanitizer.Sanitize(request.State)!;
        property.PostalCode = _sanitizer.Sanitize(request.PostalCode)!;
        property.Country = _sanitizer.Sanitize(request.Country)!;
        property.DefaultTargetRent = request.DefaultTargetRent;

        var submittedIds = request.Units.Where(u => u.Id.HasValue).Select(u => u.Id!.Value).ToHashSet();

        // Units removed from the submitted list are gone from the form -- soft-deleted the
        // same way any other ISoftDelete entity is (AuditSaveChangesInterceptor converts the
        // Remove() below into IsDeleted = true, not a real DELETE).
        foreach (var existingUnit in property.Units.Where(u => !submittedIds.Contains(u.Id)).ToList())
        {
            _dbContext.Units.Remove(existingUnit);
        }

        foreach (var unitRequest in request.Units)
        {
            var existingUnit = unitRequest.Id.HasValue
                ? property.Units.FirstOrDefault(u => u.Id == unitRequest.Id.Value)
                : null;

            if (existingUnit is not null)
            {
                existingUnit.UnitIdentifier = _sanitizer.Sanitize(unitRequest.UnitIdentifier)!;
                existingUnit.TargetRent = unitRequest.TargetRent;
                existingUnit.OccupancyStatus = unitRequest.OccupancyStatus;
            }
            else
            {
                // Explicitly Add()-ed to the DbSet with PropertyId set directly, rather than
                // relying on navigation-collection fixup (property.Units.Add(...)) -- mixed
                // in the same SaveChanges with an edited sibling unit and a removed one, that
                // implicit-graph-tracking path was observed to leave this new Unit's entry
                // out of ApplyTenantStamping's pass entirely, so it never got its TenantId
                // stamped and failed the tenant-ownership check instead.
                _dbContext.Units.Add(new Domain.Entities.Unit
                {
                    Id = Guid.NewGuid(),
                    PropertyId = property.Id,
                    UnitIdentifier = _sanitizer.Sanitize(unitRequest.UnitIdentifier)!,
                    TargetRent = unitRequest.TargetRent ?? request.DefaultTargetRent,
                    OccupancyStatus = unitRequest.OccupancyStatus,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(property));
    }

    private static void ValidateRequest(UpsertPropertyRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["Name"] = ["Property name is required."];
        }

        if (string.IsNullOrWhiteSpace(request.StreetAddress1))
        {
            errors["StreetAddress1"] = ["Street address is required."];
        }

        if (string.IsNullOrWhiteSpace(request.City))
        {
            errors["City"] = ["City is required."];
        }

        if (string.IsNullOrWhiteSpace(request.State))
        {
            errors["State"] = ["State is required."];
        }

        if (string.IsNullOrWhiteSpace(request.PostalCode))
        {
            errors["PostalCode"] = ["Postal code is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Country))
        {
            errors["Country"] = ["Country is required."];
        }

        if (request.DefaultTargetRent is < 0)
        {
            errors["DefaultTargetRent"] = ["Default target rent cannot be negative."];
        }

        for (var i = 0; i < request.Units.Count; i++)
        {
            var unit = request.Units[i];
            if (string.IsNullOrWhiteSpace(unit.UnitIdentifier))
            {
                errors[$"Units[{i}].UnitIdentifier"] = ["Unit identifier is required."];
            }

            if (unit.TargetRent is < 0)
            {
                errors[$"Units[{i}].TargetRent"] = ["Target rent cannot be negative."];
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static PropertyResponse ToResponse(Property property) => new(
        property.Id,
        property.Name,
        property.PropertyType,
        property.StreetAddress1,
        property.StreetAddress2,
        property.City,
        property.State,
        property.PostalCode,
        property.Country,
        property.DefaultTargetRent,
        property.Units
            // A soft-deleted unit stays in this in-memory navigation collection after
            // SaveChanges (AuditSaveChangesInterceptor mutates IsDeleted in place, it doesn't
            // remove the instance from Property.Units) -- filtered out here the same way the
            // global query filter would exclude it from a fresh query.
            .Where(u => !u.IsDeleted)
            .Select(u => new UnitResponse(u.Id, u.UnitIdentifier, u.TargetRent, u.OccupancyStatus))
            .ToList());
}
