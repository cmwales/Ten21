namespace Ten21.Application.Abstractions;

/// <summary>
/// US-21: mechanically extracts the 9 expected columns (PropertyName, PropertyType,
/// StreetAddress1, City, State, PostalCode, Country, UnitIdentifier, TargetRent) from a
/// .csv or .xlsx file by header name -- no validation, no sanitization, just text
/// extraction. RowNumber matches what a user sees opening the file in Excel/Sheets (header
/// = row 1, first data row = row 2), for error messages like "Row 42: ...".
/// </summary>
public interface IPropertyImportFileParser
{
    IReadOnlyList<RawImportRow> Parse(Stream fileStream, string fileName);
}

public record RawImportRow(
    int RowNumber,
    string PropertyName,
    string PropertyType,
    string StreetAddress1,
    string City,
    string State,
    string PostalCode,
    string Country,
    string UnitIdentifier,
    string TargetRent);
