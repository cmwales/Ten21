using System.Text;
using ClosedXML.Excel;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Import;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-21: proves the CSV and XLSX branches extract identical data by header name
/// (not column position), and that a file missing a required header is rejected up front
/// rather than silently producing blank rows.</summary>
public class PropertyImportFileParserTests
{
    private readonly PropertyImportFileParser _parser = new();

    private const string ValidCsv = """
        PropertyName,PropertyType,StreetAddress1,City,State,PostalCode,Country,UnitIdentifier,TargetRent
        Riverside Apartments,MultiFamily,100 Main St,Provo,UT,84601,USA,101,1200
        Riverside Apartments,MultiFamily,100 Main St,Provo,UT,84601,USA,102,1250
        """;

    [Fact]
    public void Parse_Csv_ReturnsOneRowPerDataLine_WithExcelStyleRowNumbers()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ValidCsv));

        var rows = _parser.Parse(stream, "properties.csv");

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].RowNumber); // header is row 1, first data row is row 2
        Assert.Equal("Riverside Apartments", rows[0].PropertyName);
        Assert.Equal("101", rows[0].UnitIdentifier);
        Assert.Equal(3, rows[1].RowNumber);
        Assert.Equal("102", rows[1].UnitIdentifier);
    }

    [Fact]
    public void Parse_Csv_ColumnOrderDoesNotMatter_AsLongAsHeadersArePresent()
    {
        const string reordered = """
            UnitIdentifier,TargetRent,PropertyName,PropertyType,StreetAddress1,City,State,PostalCode,Country
            101,1200,Riverside Apartments,MultiFamily,100 Main St,Provo,UT,84601,USA
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(reordered));

        var rows = _parser.Parse(stream, "properties.csv");

        var row = Assert.Single(rows);
        Assert.Equal("Riverside Apartments", row.PropertyName);
        Assert.Equal("101", row.UnitIdentifier);
        Assert.Equal("1200", row.TargetRent);
    }

    [Fact]
    public void Parse_Csv_ThrowsValidationException_WhenARequiredHeaderIsMissing()
    {
        const string missingCountry = """
            PropertyName,PropertyType,StreetAddress1,City,State,PostalCode,UnitIdentifier,TargetRent
            Riverside Apartments,MultiFamily,100 Main St,Provo,UT,84601,101,1200
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(missingCountry));

        var ex = Assert.Throws<ValidationException>(() => _parser.Parse(stream, "properties.csv"));
        Assert.Contains("Country", ex.Errors["File"][0]);
    }

    [Fact]
    public void Parse_Xlsx_ReturnsTheSameDataAsTheEquivalentCsv()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Properties");
        string[] headers =
        [
            "PropertyName", "PropertyType", "StreetAddress1", "City", "State",
            "PostalCode", "Country", "UnitIdentifier", "TargetRent",
        ];
        for (var i = 0; i < headers.Length; i++)
        {
            worksheet.Cell(1, i + 1).Value = headers[i];
        }
        worksheet.Cell(2, 1).Value = "Riverside Apartments";
        worksheet.Cell(2, 2).Value = "MultiFamily";
        worksheet.Cell(2, 3).Value = "100 Main St";
        worksheet.Cell(2, 4).Value = "Provo";
        worksheet.Cell(2, 5).Value = "UT";
        worksheet.Cell(2, 6).Value = "84601";
        worksheet.Cell(2, 7).Value = "USA";
        worksheet.Cell(2, 8).Value = "101";
        worksheet.Cell(2, 9).Value = "1200";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var rows = _parser.Parse(stream, "properties.xlsx");

        var row = Assert.Single(rows);
        Assert.Equal(2, row.RowNumber);
        Assert.Equal("Riverside Apartments", row.PropertyName);
        Assert.Equal("101", row.UnitIdentifier);
    }

    [Fact]
    public void Parse_UnsupportedExtension_ThrowsValidationException()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a spreadsheet"));

        Assert.Throws<ValidationException>(() => _parser.Parse(stream, "properties.txt"));
    }
}
