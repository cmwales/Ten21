using Microsoft.EntityFrameworkCore;
using Ten21.Application.Abstractions;
using Ten21.Domain.Entities;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.UnitGroups;

/// <summary>Business-layer refactor: extracted from UnitGroupsController. No repository --
/// every query here is a single-table find/list. No interface -- same reasoning as
/// ChargeService/PaymentService.</summary>
public class UnitGroupService
{
    private readonly Ten21DbContext _dbContext;
    private readonly IInputSanitizer _sanitizer;

    public UnitGroupService(Ten21DbContext dbContext, IInputSanitizer sanitizer)
    {
        _dbContext = dbContext;
        _sanitizer = sanitizer;
    }

    public async Task<IReadOnlyList<UnitGroupResponse>> GetUnitGroupsAsync(CancellationToken cancellationToken) =>
        await _dbContext.UnitGroups.AsNoTracking()
            .OrderBy(g => g.GroupName)
            .Select(g => ToResponse(g))
            .ToListAsync(cancellationToken);

    public async Task<UnitGroupResponse> GetUnitGroupAsync(Guid id, CancellationToken cancellationToken)
    {
        var group = await _dbContext.UnitGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Unit group '{id}' was not found.");

        return ToResponse(group);
    }

    public async Task<UnitGroupResponse> CreateAsync(UpsertUnitGroupRequest request, CancellationToken cancellationToken)
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

        return ToResponse(group);
    }

    public async Task<UnitGroupResponse> UpdateAsync(Guid id, UpsertUnitGroupRequest request, CancellationToken cancellationToken)
    {
        var fields = ValidateAndSanitize(request);

        var group = await _dbContext.UnitGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Unit group '{id}' was not found.");

        group.GroupName = fields.GroupName;
        group.Description = fields.Description;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(group);
    }

    /// <summary>Restrict, not Cascade, on Property.UnitGroupId (PropertyConfiguration) means a
    /// stray delete would surface as an opaque DbUpdateException/500 rather than the RFC 7807
    /// response this taxonomy guarantees -- checked explicitly here instead.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
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
