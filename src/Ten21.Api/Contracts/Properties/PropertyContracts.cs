using Ten21.Domain.Enums;

namespace Ten21.Api.Contracts.Properties;

/// <summary>
/// A single flat, standalone leasable space -- a whole single-family house, or one suite
/// within a larger building. UnitIdentifier is null/omitted for a standalone property, set
/// (e.g. "Suite A") for one of several properties sharing the same street address. There is
/// deliberately no nested/child collection here -- see Property's own class comment for why
/// this replaced an earlier Property-with-child-Units shape.
/// </summary>
public record UpsertPropertyRequest(
    string Name,
    PropertyType PropertyType,
    string StreetAddress1,
    string? StreetAddress2,
    string City,
    string State,
    string PostalCode,
    string Country,
    string? UnitIdentifier,
    decimal? TargetRent,
    OccupancyStatus OccupancyStatus,
    // US-25: defaults false (trailing, with a default) so existing callers that predate
    // this field don't need to change -- a PM must opt a property into the community
    // directory explicitly.
    bool AllowTenantDirectory = false);

public record PropertyResponse(
    Guid Id,
    string Name,
    PropertyType PropertyType,
    string StreetAddress1,
    string? StreetAddress2,
    string City,
    string State,
    string PostalCode,
    string Country,
    string? UnitIdentifier,
    decimal? TargetRent,
    OccupancyStatus OccupancyStatus,
    bool AllowTenantDirectory);

/// <summary>US-20: the "lightweight PropertyListDto" the acceptance criteria calls for --
/// drops StreetAddress2/Country (not shown in the flat list view) relative to the full
/// PropertyResponse used by the edit form.</summary>
public record PropertyListItemDto(
    Guid Id,
    string Name,
    PropertyType PropertyType,
    string StreetAddress1,
    string City,
    string State,
    string PostalCode,
    string? UnitIdentifier,
    decimal? TargetRent,
    OccupancyStatus OccupancyStatus);

/// <summary>TotalCount is always the total row count, matching the "Showing 1-15 of 42"
/// acceptance-criteria wording -- when pageSize is omitted, Items contains every property
/// and PageSize echoes TotalCount.</summary>
public record PropertyListResponse(
    IReadOnlyList<PropertyListItemDto> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);
