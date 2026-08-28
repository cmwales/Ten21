using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.UnitGroups;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-29: workspace-scoped physical section/phase catalog backing the unit tiers/groups
/// matrix editor. Property Manager manages, Property Owner reads -- same Permissions.Property.*
/// claims the matrix and property list already use, no new permission category needed.
/// </summary>
[ApiController]
[Route("api/unit-groups")]
public class UnitGroupsController : ControllerBase
{
    private readonly Ten21DbContext _dbContext;
    private readonly IInputSanitizer _sanitizer;

    public UnitGroupsController(Ten21DbContext dbContext, IInputSanitizer sanitizer)
    {
        _dbContext = dbContext;
        _sanitizer = sanitizer;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Property.Read)]
    public async Task<IActionResult> GetUnitGroups(CancellationToken cancellationToken)
    {
        var groups = await _dbContext.UnitGroups.AsNoTracking()
            .OrderBy(g => g.GroupName)
            .Select(g => ToResponse(g))
            .ToListAsync(cancellationToken);

        return Ok(groups);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Read)]
    public async Task<IActionResult> GetUnitGroup(Guid id, CancellationToken cancellationToken)
    {
        var group = await _dbContext.UnitGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Unit group '{id}' was not found.");

        return Ok(ToResponse(group));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> CreateUnitGroup(
        [FromBody] UpsertUnitGroupRequest request, CancellationToken cancellationToken)
    {
        var fields = ValidateAndSanitize(request);

        var group = new UnitGroup
        {
            Id = Guid.NewGuid(),
            GroupName = fields.GroupName,
            Description = fields.Description,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _dbContext.UnitGroups.Add(group);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetUnitGroup), new { id = group.Id }, ToResponse(group));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> UpdateUnitGroup(
        Guid id, [FromBody] UpsertUnitGroupRequest request, CancellationToken cancellationToken)
    {
        var fields = ValidateAndSanitize(request);

        var group = await _dbContext.UnitGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Unit group '{id}' was not found.");

        group.GroupName = fields.GroupName;
        group.Description = fields.Description;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(group));
    }

    /// <summary>Restrict, not Cascade, on Property.UnitGroupId (PropertyConfiguration) means a
    /// stray delete would surface as an opaque DbUpdateException/500 rather than the RFC 7807
    /// response this taxonomy guarantees -- checked explicitly here instead.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> DeleteUnitGroup(Guid id, CancellationToken cancellationToken)
    {
        var group = await _dbContext.UnitGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Unit group '{id}' was not found.");

        var inUse = await _dbContext.Properties.AnyAsync(p => p.UnitGroupId == id, cancellationToken);
        if (inUse)
        {
            throw new ConflictException(
                $"Unit group '{group.GroupName}' is still assigned to one or more properties and cannot be deleted.");
        }

        _dbContext.UnitGroups.Remove(group);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private sealed record SanitizedFields(string GroupName, string? Description);

    private SanitizedFields ValidateAndSanitize(UpsertUnitGroupRequest request)
    {
        var groupName = _sanitizer.Sanitize(request.GroupName)!;
        var description = NullIfBlank(_sanitizer.Sanitize(request.Description));

        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(groupName))
        {
            errors["GroupName"] = ["Group name is required."];
        }
        else if (groupName.Length > 100)
        {
            errors["GroupName"] = ["Group name must be 100 characters or fewer."];
        }

        if (description is { Length: > 500 })
        {
            errors["Description"] = ["Description must be 500 characters or fewer."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return new SanitizedFields(groupName, description);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static UnitGroupResponse ToResponse(UnitGroup group) => new(
        group.Id, group.GroupName, group.Description);
}
