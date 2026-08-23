using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using Ten21.Application.Abstractions;
using ValidationException = Ten21.Domain.Exceptions.ValidationException;

namespace Ten21.Infrastructure.Import;

/// <summary>US-21: dispatches to CsvHelper (.csv) or ClosedXML (.xlsx) by file extension.
/// Both branches read every expected column by HEADER NAME (not fixed position), so column
/// order in the uploaded file doesn't matter as long as the header row has all 9 expected
/// names -- see PropertyImportRequiredHeaders.</summary>
public class PropertyImportFileParser : IPropertyImportFileParser
{
    private static readonly string[] RequiredHeaders =
    [
        "PropertyName", "PropertyType", "StreetAddress1", "City", "State",
        "PostalCode", "Country", "UnitIdentifier", "TargetRent",
    ];

    public IReadOnlyList<RawImportRow> Parse(Stream fileStream, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return extension switch
        {
            ".csv" => ParseCsv(fileStream),
            ".xlsx" => ParseXlsx(fileStream),
            _ => throw new ValidationException(new Dictionary<string, string[]>
            {
                ["File"] = ["Only .csv and .xlsx files are supported."],
            }),
        };
    }

    private static List<RawImportRow> ParseCsv(Stream fileStream)
    {
        using var reader = new StreamReader(fileStream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        if (!csv.Read() || !csv.ReadHeader())
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["File"] = ["The file is empty or has no header row."],
            });
        }

        EnsureRequiredHeaders(csv.HeaderRecord ?? []);

        var rows = new List<RawImportRow>();
        var rowNumber = 1; // the header is row 1, so the first data row is row 2
        while (csv.Read())
        {
            rowNumber++;
            rows.Add(new RawImportRow(
                rowNumber,
                csv.GetField("PropertyName") ?? "",
                csv.GetField("PropertyType") ?? "",
                csv.GetField("StreetAddress1") ?? "",
                csv.GetField("City") ?? "",
                csv.GetField("State") ?? "",
                csv.GetField("PostalCode") ?? "",
                csv.GetField("Country") ?? "",
                csv.GetField("UnitIdentifier") ?? "",
                csv.GetField("TargetRent") ?? ""));
        }

        return rows;
    }

    private static List<RawImportRow> ParseXlsx(Stream fileStream)
    {
        using var workbook = new XLWorkbook(fileStream);
        var worksheet = workbook.Worksheets.First();

        var headerRow = worksheet.Row(1);
        var columnByHeader = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            columnByHeader[cell.GetString().Trim()] = cell.Address.ColumnNumber;
        }

        EnsureRequiredHeaders(columnByHeader.Keys);

        string GetCell(int rowNumber, string header) =>
            columnByHeader.TryGetValue(header, out var column)
                ? worksheet.Cell(rowNumber, column).GetString().Trim()
                : "";

        var rows = new List<RawImportRow>();
        var lastRowNumber = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRowNumber; rowNumber++)
        {
            if (worksheet.Row(rowNumber).IsEmpty())
            {
                continue;
            }

            rows.Add(new RawImportRow(
                rowNumber,
                GetCell(rowNumber, "PropertyName"),
                GetCell(rowNumber, "PropertyType"),
                GetCell(rowNumber, "StreetAddress1"),
                GetCell(rowNumber, "City"),
                GetCell(rowNumber, "State"),
                GetCell(rowNumber, "PostalCode"),
                GetCell(rowNumber, "Country"),
                GetCell(rowNumber, "UnitIdentifier"),
                GetCell(rowNumber, "TargetRent")));
        }

        return rows;
    }

    private static void EnsureRequiredHeaders(IEnumerable<string> headers)
    {
        var present = new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);
        var missing = RequiredHeaders.Where(h => !present.Contains(h)).ToArray();

        if (missing.Length > 0)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["File"] = [$"Missing required column(s): {string.Join(", ", missing)}."],
            });
        }
    }
}
