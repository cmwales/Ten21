using Ten21.Domain.Enums;

namespace Ten21.Api.Contracts.Properties;

public record UnitRequest(
    Guid? Id,
    string UnitIdentifier,
    decimal? TargetRent,
    OccupancyStatus OccupancyStatus);

public record UpsertPropertyRequest(
    string Name,
    PropertyType PropertyType,
    string StreetAddress1,
    string? StreetAddress2,
    string City,
    string State,
    string PostalCode,
    string Country,
    decimal? DefaultTargetRent,
    IReadOnlyList<UnitRequest> Units);

public record UnitResponse(
    Guid Id,
    string UnitIdentifier,
    decimal? TargetRent,
    OccupancyStatus OccupancyStatus);

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
    decimal? DefaultTargetRent,
    IReadOnlyList<UnitResponse> Units);

/// <summary>US-20: the per-unit row nested under a PropertyListItemDto.</summary>
public record PropertyListUnitDto(
    Guid Id,
    string UnitIdentifier,
    OccupancyStatus OccupancyStatus,
    decimal? TargetRent);

/// <summary>US-20: the "lightweight PropertyListDto" the acceptance criteria calls for --
/// drops StreetAddress2/Country/DefaultTargetRent (not shown in the list view) relative to
/// the full PropertyResponse used by the US-19 edit form.</summary>
public record PropertyListItemDto(
    Guid Id,
    string Name,
    PropertyType PropertyType,
    string StreetAddress1,
    string City,
    string State,
    string PostalCode,
    IReadOnlyList<PropertyListUnitDto> Units);

/// <summary>US-20: TotalCount is always the total PROPERTY count (matching the "Showing
/// 1-15 of 42 properties" acceptance-criteria wording), independent of whether pageSize was
/// supplied -- when it's omitted, Items contains every property and PageSize echoes
/// TotalCount.</summary>
public record PropertyListResponse(
    IReadOnlyList<PropertyListItemDto> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);
