using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Properties;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;
using DomainPropertyType = Ten21.Domain.Enums.PropertyType;
using DomainOccupancyStatus = Ten21.Domain.Enums.OccupancyStatus;

namespace Ten21.Api.Controllers;

/// <summary>
/// The real Property CRUD surface, replacing the throwaway US-01 proof-of-concept this
/// controller used to be. Property is a flat, standalone leasable space -- a whole
/// single-family house, or one suite within a larger building -- with no separate child
/// Unit entity. An earlier design (US-19-22) had Property own a collection of child Units;
/// tester feedback reversed that: "Each suite in a building needs to be a new property.
/// They need to be setup independently." See User_Stories_Sprint_3.md's "Flatten
/// Property/Unit" addendum for the full history.
/// </summary>
[ApiController]
[Route("api/properties")]
public class PropertiesController : ControllerBase
{
    private const long MaxImportFileSizeBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedImportExtensions = [".csv", ".xlsx"];

    private readonly Ten21DbContext _dbContext;
    private readonly IInputSanitizer _sanitizer;
    private readonly IPropertyImportFileParser _importParser;
    private readonly IHardDeleteOverride _hardDeleteOverride;

    public PropertiesController(
        Ten21DbContext dbContext,
        IInputSanitizer sanitizer,
        IPropertyImportFileParser importParser,
        IHardDeleteOverride hardDeleteOverride)
    {
        _dbContext = dbContext;
        _sanitizer = sanitizer;
        _importParser = importParser;
        _hardDeleteOverride = hardDeleteOverride;
    }

    /// <summary>
    /// pageNumber/pageSize are both optional. Omitting pageSize returns every property,
    /// unpaginated -- the Angular list view does its own client-side search/pagination over
    /// that full set (a debounced search across a server-paginated page wouldn't be able to
    /// search rows outside the current page), so this is what the frontend actually calls;
    /// pageNumber/pageSize exist for direct API consumers that want real server-side paging.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.Property.Read)]
    public async Task<IActionResult> GetProperties(
        [FromQuery] int? pageNumber, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        // No manual .Where(p => p.TenantId == ...) anywhere in this method -- the global
        // query filter in Ten21DbContext does it automatically.
        var query = _dbContext.Properties.OrderBy(p => p.Name).AsQueryable();

        var totalCount = await query.CountAsync(cancellationToken);

        var effectivePageNumber = pageNumber is > 0 ? pageNumber.Value : 1;
        if (pageSize is > 0)
        {
            query = query.Skip((effectivePageNumber - 1) * pageSize.Value).Take(pageSize.Value);
        }

        // Projected directly in the SQL SELECT -- unlike GetProperty/CreateProperty/
        // UpdateProperty, this action never needs the full Property entity, and
        // PropertyListItemDto's field list is a straight subset with no nested-collection
        // mapping, so (now that Property is flat) EF Core can translate the projection
        // itself instead of materializing every column and mapping in memory. (An earlier
        // version of this action DID need ToListAsync() first, back when the mapping method
        // walked a nested Units collection EF Core couldn't translate -- see
        // User_Stories_Sprint_3.md's US-20 section for that now-resolved history.)
        var items = await query
            .Select(p => new PropertyListItemDto(
                p.Id, p.Name, p.PropertyType, p.StreetAddress1, p.City, p.State, p.PostalCode,
                p.UnitIdentifier, p.TargetRent, p.OccupancyStatus))
            .ToListAsync(cancellationToken);

        return Ok(new PropertyListResponse(items, totalCount, effectivePageNumber, pageSize ?? totalCount));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Read)]
    public async Task<IActionResult> GetProperty(Guid id, CancellationToken cancellationToken)
    {
        var property = await _dbContext.Properties
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
        var fields = SanitizeAddressFields(request);
        await EnsureNoDuplicatePropertyAsync(fields, excludingId: null, cancellationToken);

        var property = new Property
        {
            Id = Guid.NewGuid(),
            Name = fields.Name,
            PropertyType = request.PropertyType,
            StreetAddress1 = fields.StreetAddress1,
            StreetAddress2 = fields.StreetAddress2,
            City = fields.City,
            State = fields.State,
            PostalCode = fields.PostalCode,
            Country = fields.Country,
            UnitIdentifier = fields.UnitIdentifier,
            TargetRent = request.TargetRent,
            OccupancyStatus = request.OccupancyStatus,
            CreatedAt = DateTimeOffset.UtcNow,
        };

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
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Property '{id}' was not found.");

        var fields = SanitizeAddressFields(request);
        await EnsureNoDuplicatePropertyAsync(fields, excludingId: id, cancellationToken);

        property.Name = fields.Name;
        property.PropertyType = request.PropertyType;
        property.StreetAddress1 = fields.StreetAddress1;
        property.StreetAddress2 = fields.StreetAddress2;
        property.City = fields.City;
        property.State = fields.State;
        property.PostalCode = fields.PostalCode;
        property.Country = fields.Country;
        property.UnitIdentifier = fields.UnitIdentifier;
        property.TargetRent = request.TargetRent;
        property.OccupancyStatus = request.OccupancyStatus;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(property));
    }

    /// <summary>
    /// Shared by Create and Update -- Import has its own separate sanitization pipeline
    /// (FormulaInjectionGuard, row-level validation errors) over a different source type
    /// (SanitizedImportRow, not UpsertPropertyRequest), so it isn't folded into this one;
    /// forcing all three through a single method would mean adapting Import's already-valid
    /// data into a fake UpsertPropertyRequest just to satisfy this method's shape.
    /// </summary>
    private SanitizedAddressFields SanitizeAddressFields(UpsertPropertyRequest request) => new(
        _sanitizer.Sanitize(request.Name)!,
        _sanitizer.Sanitize(request.StreetAddress1)!,
        _sanitizer.Sanitize(request.StreetAddress2),
        _sanitizer.Sanitize(request.City)!,
        _sanitizer.Sanitize(request.State)!,
        _sanitizer.Sanitize(request.PostalCode)!,
        _sanitizer.Sanitize(request.Country)!,
        NullIfBlank(_sanitizer.Sanitize(request.UnitIdentifier)));

    private sealed record SanitizedAddressFields(
        string Name,
        string StreetAddress1,
        string? StreetAddress2,
        string City,
        string State,
        string PostalCode,
        string Country,
        string? UnitIdentifier);

    /// <summary>
    /// "Each place that can be rented should represent a property" cuts both ways: a duplex
    /// half and its sibling must both get their own row (no re-merging back into one), but
    /// the exact same address+unit combination should never exist twice for one tenant --
    /// most likely a re-submitted form or a re-imported CSV, not two real properties. Not
    /// backed by a DB-level unique constraint (yet) -- this is the single app-level gate for
    /// both the interactive form and the bulk importer (see MarkDuplicateRowsAsync).
    /// </summary>
    private async Task EnsureNoDuplicatePropertyAsync(
        SanitizedAddressFields fields, Guid? excludingId, CancellationToken cancellationToken)
    {
        var duplicateExists = await _dbContext.Properties.AnyAsync(p =>
            p.Id != (excludingId ?? Guid.Empty) &&
            p.StreetAddress1 == fields.StreetAddress1 &&
            p.City == fields.City &&
            p.State == fields.State &&
            p.PostalCode == fields.PostalCode &&
            p.Country == fields.Country &&
            p.UnitIdentifier == fields.UnitIdentifier,
            cancellationToken);

        if (duplicateExists)
        {
            throw new ConflictException(fields.UnitIdentifier is null
                ? $"A property already exists at {fields.StreetAddress1}, {fields.City}, {fields.State} {fields.PostalCode}."
                : $"A property already exists at {fields.StreetAddress1}, {fields.City}, {fields.State} {fields.PostalCode} with unit identifier '{fields.UnitIdentifier}'.");
        }
    }

    /// <summary>
    /// US-22: zero applied payments -> a genuine hard delete (opted out of
    /// AuditSaveChangesInterceptor's default soft-delete conversion via
    /// IHardDeleteOverride); applied payments -> a soft delete (IsDeleted = true, excluded
    /// via the existing global query filter). Property has no child entities to cascade to
    /// now that Unit no longer exists, so this is a single, simple Remove().
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Delete)]
    public async Task<IActionResult> DeleteProperty(Guid id, CancellationToken cancellationToken)
    {
        var property = await _dbContext.Properties
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Property '{id}' was not found.");

        var hasAppliedPayments = await HasAppliedPaymentsAsync(id, cancellationToken);
        if (!hasAppliedPayments)
        {
            _hardDeleteOverride.MarkForHardDelete(property);
        }

        _dbContext.Properties.Remove(property);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// US-22: placeholder until Phase 1 (Monetization & Billing Logic) ships a real payment
    /// ledger -- see User_Stories_Sprint_3.md's Executive Summary for why this is a
    /// deliberate, confirmed choice rather than an assumption. Always false today, meaning
    /// every delete currently takes the hard-delete branch above. When a real ledger exists,
    /// swap this method's body for the genuine
    /// PaymentLedger.AnyAsync(x => x.PropertyId == propertyId && x.AmountPaid > 0) query --
    /// nothing else in DeleteProperty needs to change.
    /// </summary>
    private Task<bool> HasAppliedPaymentsAsync(Guid propertyId, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    /// <summary>
    /// US-21: parses, sanitizes, and validates every row up front -- nothing is added to the
    /// DbContext until every row has passed. A single invalid row means the whole file is
    /// rejected (Success = false, PropertiesCreated = 0, nothing persisted); only once every
    /// row is valid does this open an explicit transaction, insert everything, and commit.
    /// One row = one flat Property (no more grouping rows sharing an address into a parent
    /// with child units). The buffer is never written to disk or object storage --
    /// IFormFile.OpenReadStream() is parsed directly from request memory and discarded once
    /// this action returns.
    /// </summary>
    [HttpPost("import")]
    [Authorize(Policy = Permissions.Property.Import)]
    [RequestSizeLimit(MaxImportFileSizeBytes)]
    public async Task<IActionResult> ImportProperties(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["File"] = ["A .csv or .xlsx file is required."],
            });
        }

        if (file.Length > MaxImportFileSizeBytes)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["File"] = [$"File size must not exceed {MaxImportFileSizeBytes / (1024 * 1024)}MB."],
            });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedImportExtensions.Contains(extension))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["File"] = ["Only .csv and .xlsx files are supported."],
            });
        }

        List<RawImportRow> rawRows;
        await using (var stream = file.OpenReadStream())
        {
            rawRows = _importParser.Parse(stream, file.FileName).ToList();
        }

        var sanitizedRows = rawRows.Select(SanitizeAndValidateRow).ToList();
        sanitizedRows = await MarkDuplicateRowsAsync(sanitizedRows, cancellationToken);
        var invalidRowCount = sanitizedRows.Count(r => !r.IsValid);
        var resultRows = sanitizedRows.Select(ToImportRowResult).ToList();

        if (invalidRowCount > 0)
        {
            return Ok(new ImportPropertiesResponse(
                Success: false,
                TotalRows: sanitizedRows.Count,
                InvalidRowCount: invalidRowCount,
                PropertiesCreated: 0,
                Rows: resultRows));
        }

        var propertiesCreated = await PersistImportedRowsAsync(sanitizedRows, cancellationToken);

        return Ok(new ImportPropertiesResponse(
            Success: true,
            TotalRows: sanitizedRows.Count,
            InvalidRowCount: 0,
            PropertiesCreated: propertiesCreated,
            Rows: resultRows));
    }

    private async Task<int> PersistImportedRowsAsync(
        IReadOnlyList<SanitizedImportRow> rows, CancellationToken cancellationToken)
    {
        // Explicit transaction per the acceptance criteria's own wording, even though a
        // single SaveChangesAsync call below is already atomic on its own -- this also
        // protects against a DB-level failure (e.g. a constraint violation) that only
        // surfaces after every row already passed application-level validation above.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        foreach (var row in rows)
        {
            _dbContext.Properties.Add(new Property
            {
                Id = Guid.NewGuid(),
                Name = row.PropertyName,
                PropertyType = row.PropertyType!.Value,
                StreetAddress1 = row.StreetAddress1,
                City = row.City,
                State = row.State,
                PostalCode = row.PostalCode,
                Country = row.Country,
                UnitIdentifier = NullIfBlank(row.UnitIdentifier),
                TargetRent = row.TargetRent,
                OccupancyStatus = DomainOccupancyStatus.Vacant,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return rows.Count;
    }

    private SanitizedImportRow SanitizeAndValidateRow(RawImportRow raw)
    {
        var propertyName = FormulaInjectionGuard.Sanitize(_sanitizer.Sanitize(raw.PropertyName)!.Trim());
        var streetAddress1 = FormulaInjectionGuard.Sanitize(_sanitizer.Sanitize(raw.StreetAddress1)!.Trim());
        var city = FormulaInjectionGuard.Sanitize(_sanitizer.Sanitize(raw.City)!.Trim());
        var state = FormulaInjectionGuard.Sanitize(_sanitizer.Sanitize(raw.State)!.Trim());
        var postalCode = FormulaInjectionGuard.Sanitize(_sanitizer.Sanitize(raw.PostalCode)!.Trim());
        var country = FormulaInjectionGuard.Sanitize(_sanitizer.Sanitize(raw.Country)!.Trim());
        var unitIdentifier = FormulaInjectionGuard.Sanitize(_sanitizer.Sanitize(raw.UnitIdentifier)!.Trim());

        var errors = new List<string>();

        if (propertyName.Length == 0)
        {
            errors.Add("Property Name is required.");
        }

        DomainPropertyType? propertyType = null;
        if (raw.PropertyType.Trim().Length == 0)
        {
            errors.Add("Property Type is required.");
        }
        else if (!Enum.TryParse<DomainPropertyType>(raw.PropertyType.Trim(), ignoreCase: true, out var parsedType))
        {
            errors.Add($"Property Type '{raw.PropertyType}' is not valid.");
        }
        else
        {
            propertyType = parsedType;
        }

        if (streetAddress1.Length == 0)
        {
            errors.Add("Street Address 1 is required.");
        }

        if (city.Length == 0)
        {
            errors.Add("City is required.");
        }

        if (state.Length == 0)
        {
            errors.Add("State is required.");
        }

        if (postalCode.Length == 0)
        {
            errors.Add("Postal Code is required.");
        }

        if (country.Length == 0)
        {
            errors.Add("Country is required.");
        }

        // UnitIdentifier is optional here -- a flat Property row may be a standalone
        // single-family home with no suite/unit number at all.

        decimal? targetRent = null;
        var rawTargetRent = raw.TargetRent.Trim();
        if (rawTargetRent.Length > 0)
        {
            if (!decimal.TryParse(rawTargetRent, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedRent) || parsedRent <= 0)
            {
                errors.Add("Target Rent must be a positive number.");
            }
            else
            {
                targetRent = parsedRent;
            }
        }

        return new SanitizedImportRow(
            raw.RowNumber, propertyName, propertyType, streetAddress1, city, state, postalCode, country,
            unitIdentifier, targetRent, raw.TargetRent, errors.Count == 0, errors);
    }

    /// <summary>
    /// Import-side counterpart to EnsureNoDuplicatePropertyAsync -- catches both a row that
    /// duplicates an existing DB property (e.g. the same file re-uploaded after a timed-out
    /// request the user thought had failed) and two rows within the same file that duplicate
    /// each other. One query fetches every existing property sharing any of this batch's
    /// street addresses up front, rather than a per-row round trip.
    /// </summary>
    private async Task<List<SanitizedImportRow>> MarkDuplicateRowsAsync(
        List<SanitizedImportRow> rows, CancellationToken cancellationToken)
    {
        var addresses = rows.Where(r => r.IsValid).Select(r => r.StreetAddress1).Distinct().ToList();

        var existingKeys = (await _dbContext.Properties
                .Where(p => addresses.Contains(p.StreetAddress1))
                .Select(p => new { p.StreetAddress1, p.City, p.State, p.PostalCode, p.Country, p.UnitIdentifier })
                .ToListAsync(cancellationToken))
            .Select(p => (p.StreetAddress1, p.City, p.State, p.PostalCode, p.Country, p.UnitIdentifier))
            .ToHashSet();

        var seenInFile = new HashSet<(string, string, string, string, string, string?)>();
        var result = new List<SanitizedImportRow>(rows.Count);

        foreach (var row in rows)
        {
            if (!row.IsValid)
            {
                result.Add(row);
                continue;
            }

            var normalizedUnitIdentifier = NullIfBlank(row.UnitIdentifier);
            var key = (row.StreetAddress1, row.City, row.State, row.PostalCode, row.Country, normalizedUnitIdentifier);

            if (!seenInFile.Add(key))
            {
                result.Add(row with
                {
                    IsValid = false,
                    Errors = [.. row.Errors, "Duplicate of another row in this file (same address and unit identifier)."],
                });
            }
            else if (existingKeys.Contains(key))
            {
                result.Add(row with
                {
                    IsValid = false,
                    Errors = [.. row.Errors, "A property with this address and unit identifier already exists."],
                });
            }
            else
            {
                result.Add(row);
            }
        }

        return result;
    }

    private static ImportRowResult ToImportRowResult(SanitizedImportRow row) => new(
        row.RowNumber,
        row.PropertyName,
        row.PropertyType?.ToString() ?? "",
        row.StreetAddress1,
        row.City,
        row.State,
        row.PostalCode,
        row.Country,
        row.UnitIdentifier,
        row.RawTargetRent,
        row.IsValid,
        row.Errors);

    private sealed record SanitizedImportRow(
        int RowNumber,
        string PropertyName,
        DomainPropertyType? PropertyType,
        string StreetAddress1,
        string City,
        string State,
        string PostalCode,
        string Country,
        string UnitIdentifier,
        decimal? TargetRent,
        string RawTargetRent,
        bool IsValid,
        IReadOnlyList<string> Errors);

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

        if (request.TargetRent is < 0)
        {
            errors["TargetRent"] = ["Target rent cannot be negative."];
        }

        // PropertyConfiguration caps this column at 50 chars -- without this check, an
        // over-length value passes validation here and fails at SaveChangesAsync instead,
        // as an unhandled DbUpdateException/PostgresException (500) rather than the RFC
        // 7807 ValidationException response this taxonomy is supposed to guarantee.
        if (request.UnitIdentifier is { Length: > 50 })
        {
            errors["UnitIdentifier"] = ["Unit identifier must be 50 characters or fewer."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

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
        property.UnitIdentifier,
        property.TargetRent,
        property.OccupancyStatus);
}
