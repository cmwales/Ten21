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
