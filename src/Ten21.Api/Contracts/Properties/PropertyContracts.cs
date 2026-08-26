using Ten21.Domain.Enums;

namespace Ten21.Api.Contracts.Properties;

/// <summary>US-29: a single row's matrix-editor state -- sent as a full snapshot on
/// blur/change (same "full replace, not true PATCH" convention as UpdateProperty) rather
/// than field-by-field, so the auto-save call is one request regardless of which column
/// changed.</summary>
public record UpdatePropertyMatrixRowRequest(
    Guid? UnitGroupId,
    Guid? UnitTierId,
    decimal? TargetRent);

/// <summary>US-29: applies one column to many rows at once. Field names which column so a
/// null ValueId can mean "clear it" rather than being ambiguous with "leave it alone" --
/// see MatrixBatchField's own doc comment. Assigning a tier also overwrites TargetRent on
/// every targeted row to the tier's DefaultRent (there's no per-row prefill step to
/// override in a batch action, unlike the single-row inline edit); assigning a group never
/// touches TargetRent.</summary>
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

/// <summary>
/// Moved here from Lease in a post-Sprint-6 fix: tester feedback was that a move-out notice
/// is a per-unit fact ("when do I need to find a new tenant"), not a per-resident one -- a
/// two-occupant unit where one person gives notice doesn't mean the unit is vacating.
/// Applies uniformly to every Lease on this property (LeasesController.ComputeEffectiveStatus/
/// ComputeIsExpiringSoon read it from here now). A dedicated PATCH rather than folding into
/// UpsertPropertyRequest -- this is an operational lease-lifecycle update the PM makes from
/// the Lease drawer, not part of the property's own identity form.
/// </summary>
public record UpdateMoveOutNoticeRequest(DateOnly? MoveOutNoticeDate);

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
    bool AllowTenantDirectory,
    Guid? UnitGroupId = null,
    Guid? UnitTierId = null,
    DateOnly? MoveOutNoticeDate = null);

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
    OccupancyStatus OccupancyStatus,
    // US-29: included so the matrix editor can reuse this same unpaginated list endpoint
    // as its row source instead of a second GET endpoint returning near-identical data.
    Guid? UnitGroupId = null,
    Guid? UnitTierId = null);

/// <summary>TotalCount is always the total row count, matching the "Showing 1-15 of 42"
/// acceptance-criteria wording -- when pageSize is omitted, Items contains every property
/// and PageSize echoes TotalCount.</summary>
public record PropertyListResponse(
    IReadOnlyList<PropertyListItemDto> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);
