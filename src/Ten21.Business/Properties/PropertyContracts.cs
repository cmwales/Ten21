using Ten21.Domain.Enums;

namespace Ten21.Business.Properties;

/// <summary>Business-layer refactor: relocated from Ten21.Api.Contracts.Properties.</summary>
public record UpdatePropertyMatrixRowRequest(
    Guid? UnitGroupId,
    Guid? UnitTierId,
    decimal? TargetRent);

public record BatchAssignMatrixRequest(
    IReadOnlyList<Guid> PropertyIds,
    MatrixBatchField Field,
    Guid? ValueId);

public record PropertyMatrixRowResponse(
    Guid Id,
    string? UnitIdentifier,
    Guid? UnitGroupId,
    Guid? UnitTierId,
    decimal? TargetRent);

public record UpdateMoveOutNoticeRequest(DateOnly? MoveOutNoticeDate);

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
    bool AllowTenantDirectory,
    Guid? UnitGroupId = null,
    Guid? UnitTierId = null,
    DateOnly? MoveOutNoticeDate = null);

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
    OccupancyStatus OccupancyStatus,
    Guid? UnitGroupId = null,
    Guid? UnitTierId = null);

public record PropertyListResponse(
    IReadOnlyList<PropertyListItemDto> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);

public record ImportRowResult(
    int RowNumber,
    string PropertyName,
    string PropertyType,
    string StreetAddress1,
    string City,
    string State,
    string PostalCode,
    string Country,
    string UnitIdentifier,
    string TargetRent,
    bool IsValid,
    IReadOnlyList<string> Errors);

public record ImportPropertiesResponse(
    bool Success,
    int TotalRows,
    int InvalidRowCount,
    int PropertiesCreated,
    IReadOnlyList<ImportRowResult> Rows);
