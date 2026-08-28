using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.UnitTiers;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-29: workspace-scoped pricing tier catalog backing the unit tiers/groups matrix editor.
/// Property Manager manages, Property Owner reads -- same Permissions.Property.* claims the
/// matrix and property list already use, no new permission category needed.
/// </summary>
[ApiController]
[Route("api/unit-tiers")]
public class UnitTiersController : ControllerBase
{
    private readonly Ten21DbContext _dbContext;
    private readonly IInputSanitizer _sanitizer;

    public UnitTiersController(Ten21DbContext dbContext, IInputSanitizer sanitizer)
    {
        _dbContext = dbContext;
        _sanitizer = sanitizer;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Property.Read)]
    public async Task<IActionResult> GetUnitTiers(CancellationToken cancellationToken)
    {
        var tiers = await _dbContext.UnitTiers.AsNoTracking()
            .OrderBy(t => t.TierName)
            .Select(t => ToResponse(t))
            .ToListAsync(cancellationToken);

        return Ok(tiers);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Read)]
    public async Task<IActionResult> GetUnitTier(Guid id, CancellationToken cancellationToken)
    {
        var tier = await _dbContext.UnitTiers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Unit tier '{id}' was not found.");

        return Ok(ToResponse(tier));
    }

    [HttpPost]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> CreateUnitTier(
        [FromBody] UpsertUnitTierRequest request, CancellationToken cancellationToken)
    {
        var fields = ValidateAndSanitize(request);

        var tier = new UnitTier
        {
            Id = Guid.NewGuid(),
            TierName = fields.TierName,
            DefaultRent = request.DefaultRent,
            AccountingCode = fields.AccountingCode,
            Description = fields.Description,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _dbContext.UnitTiers.Add(tier);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetUnitTier), new { id = tier.Id }, ToResponse(tier));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> UpdateUnitTier(
        Guid id, [FromBody] UpsertUnitTierRequest request, CancellationToken cancellationToken)
    {
        var fields = ValidateAndSanitize(request);

        var tier = await _dbContext.UnitTiers.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Unit tier '{id}' was not found.");

        tier.TierName = fields.TierName;
        tier.DefaultRent = request.DefaultRent;
        tier.AccountingCode = fields.AccountingCode;
        tier.Description = fields.Description;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(tier));
    }

    /// <summary>Restrict, not Cascade, on Property.UnitTierId (PropertyConfiguration) means a
    /// stray delete would surface as an opaque DbUpdateException/500 rather than the RFC 7807
    /// response this taxonomy guarantees -- checked explicitly here instead.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> DeleteUnitTier(Guid id, CancellationToken cancellationToken)
    {
        var tier = await _dbContext.UnitTiers.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Unit tier '{id}' was not found.");

        var inUse = await _dbContext.Properties.AnyAsync(p => p.UnitTierId == id, cancellationToken);
        if (inUse)
        {
            throw new ConflictException(
                $"Unit tier '{tier.TierName}' is still assigned to one or more properties and cannot be deleted.");
        }

        _dbContext.UnitTiers.Remove(tier);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private sealed record SanitizedFields(string TierName, string? AccountingCode, string? Description);

    private SanitizedFields ValidateAndSanitize(UpsertUnitTierRequest request)
    {
        var tierName = _sanitizer.Sanitize(request.TierName)!;
        var accountingCode = NullIfBlank(_sanitizer.Sanitize(request.AccountingCode));
        var description = NullIfBlank(_sanitizer.Sanitize(request.Description));

        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(tierName))
        {
            errors["TierName"] = ["Tier name is required."];
        }
        else if (tierName.Length > 100)
        {
            errors["TierName"] = ["Tier name must be 100 characters or fewer."];
        }

        if (request.DefaultRent < 0)
        {
            errors["DefaultRent"] = ["Default rent cannot be negative."];
        }

        if (accountingCode is { Length: > 50 })
        {
            errors["AccountingCode"] = ["Accounting code must be 50 characters or fewer."];
        }

        if (description is { Length: > 500 })
        {
            errors["Description"] = ["Description must be 500 characters or fewer."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return new SanitizedFields(tierName, accountingCode, description);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static UnitTierResponse ToResponse(UnitTier tier) => new(
        tier.Id, tier.TierName, tier.DefaultRent, tier.AccountingCode, tier.Description);
}
