namespace Ten21.Api.Contracts.Properties;

/// <summary>US-21: one parsed spreadsheet row, echoed back with its validation outcome so the
/// Angular preview grid can render the row alongside any error(s) without a second
/// round-trip.</summary>
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

/// <summary>US-21: Success is true only when every row passed validation and the batch was
/// committed in one transaction -- a single invalid row means nothing was written at all
/// (PropertiesCreated/UnitsCreated stay 0), matching "full atomic rollback if any row fails
/// validation."</summary>
public record ImportPropertiesResponse(
    bool Success,
    int TotalRows,
    int InvalidRowCount,
    int PropertiesCreated,
    int UnitsCreated,
    IReadOnlyList<ImportRowResult> Rows);
