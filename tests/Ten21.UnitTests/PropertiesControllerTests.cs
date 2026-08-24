using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Properties;
using Ten21.Api.Controllers;
using Ten21.Application.Abstractions;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Import;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Ten21.Infrastructure.Security;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>Property/Unit create + update + delete + bulk import, on the flat Property
/// model -- Property is a standalone leasable space (a whole house, or one suite within a
/// building), with no separate child Unit entity. Suite A and Suite B of the same building
/// are two independent Property rows sharing a street address, distinguished by
/// UnitIdentifier. Same in-memory SQLite pattern as TenantIsolationTests/
/// AuditSaveChangesInterceptorTests.</summary>
public class PropertiesControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HtmlInputSanitizer _sanitizer = new();
    private readonly IPropertyImportFileParser _importParser = new PropertyImportFileParser();

    public PropertiesControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, PropertiesController Controller) CreateController(Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId);
        var hardDeleteOverride = new HardDeleteOverride();

        var options = new DbContextOptionsBuilder<Ten21DbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(tenantContext, hardDeleteOverride))
            .Options;
        var db = new Ten21DbContext(options, tenantContext);
        db.Database.EnsureCreated();

        return (db, new PropertiesController(db, _sanitizer, _importParser, hardDeleteOverride));
    }

    private static UpsertPropertyRequest NewRequest(string? unitIdentifier = null) => new(
        Name: "Riverside Apartments",
        PropertyType: PropertyType.MultiFamily,
        StreetAddress1: "100 Main St",
        StreetAddress2: null,
        City: "Provo",
        State: "UT",
        PostalCode: "84601",
        Country: "USA",
        UnitIdentifier: unitIdentifier,
        TargetRent: 1200m,
        OccupancyStatus: OccupancyStatus.Vacant);

    [Fact]
    public async Task CreateProperty_PersistsAFlatProperty_WithNoChildEntities()
    {
        var (db, controller) = CreateController(Guid.NewGuid());

        var result = await controller.CreateProperty(NewRequest("Suite A"), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<PropertyResponse>(created.Value);

        Assert.Equal("Suite A", response.UnitIdentifier);
        Assert.Equal(1200m, response.TargetRent);
        Assert.Equal(OccupancyStatus.Vacant, response.OccupancyStatus);
        Assert.Equal(1, await db.Properties.CountAsync());
    }

    [Fact]
    public async Task CreateProperty_UnitIdentifierIsOptional_ForAStandaloneProperty()
    {
        var (_, controller) = CreateController(Guid.NewGuid());

        var result = await controller.CreateProperty(NewRequest(unitIdentifier: null), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<PropertyResponse>(created.Value);
        Assert.Null(response.UnitIdentifier);
    }

    [Fact]
    public async Task CreateProperty_TwoSuitesShareAnAddress_AsTwoIndependentProperties()
    {
        var (db, controller) = CreateController(Guid.NewGuid());

        await controller.CreateProperty(NewRequest("Suite A"), CancellationToken.None);
        await controller.CreateProperty(NewRequest("Suite B"), CancellationToken.None);

        var properties = await db.Properties.OrderBy(p => p.UnitIdentifier).ToListAsync();
        Assert.Equal(2, properties.Count);
        Assert.Equal("Suite A", properties[0].UnitIdentifier);
        Assert.Equal("Suite B", properties[1].UnitIdentifier);
        Assert.Equal(properties[0].StreetAddress1, properties[1].StreetAddress1);
    }

    [Fact]
    public async Task CreateProperty_StripsHtmlFromStringFields()
    {
        var (_, controller) = CreateController(Guid.NewGuid());
        var request = NewRequest() with { Name = "<script>alert(1)</script>Riverside" };

        var result = await controller.CreateProperty(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<PropertyResponse>(created.Value);
        Assert.DoesNotContain('<', response.Name);
        Assert.Contains("Riverside", response.Name);
    }

    [Fact]
    public async Task CreateProperty_ThrowsValidationException_WhenNameIsMissing()
    {
        var (_, controller) = CreateController(Guid.NewGuid());
        var request = NewRequest() with { Name = "" };

        await Assert.ThrowsAsync<ValidationException>(
            () => controller.CreateProperty(request, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateProperty_UpdatesFlatFieldsDirectly()
    {
        var (_, controller) = CreateController(Guid.NewGuid());
        var created = await controller.CreateProperty(NewRequest("Suite A"), CancellationToken.None);
        var propertyId = Assert.IsType<PropertyResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var updateRequest = NewRequest("Suite A-Renamed") with { TargetRent = 1500m, OccupancyStatus = OccupancyStatus.Occupied };
        var updateResult = await controller.UpdateProperty(propertyId, updateRequest, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(updateResult);
        var response = Assert.IsType<PropertyResponse>(ok.Value);
        Assert.Equal("Suite A-Renamed", response.UnitIdentifier);
        Assert.Equal(1500m, response.TargetRent);
        Assert.Equal(OccupancyStatus.Occupied, response.OccupancyStatus);
    }

    [Fact]
    public async Task CreateProperty_ThrowsConflict_ForExactDuplicateAddressAndUnitIdentifier()
    {
        var (_, controller) = CreateController(Guid.NewGuid());
        await controller.CreateProperty(NewRequest("Suite A"), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(
            () => controller.CreateProperty(NewRequest("Suite A"), CancellationToken.None));
    }

    [Fact]
    public async Task CreateProperty_AllowsSameAddress_WhenUnitIdentifierDiffers()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        await controller.CreateProperty(NewRequest("Suite A"), CancellationToken.None);

        await controller.CreateProperty(NewRequest("Suite B"), CancellationToken.None);

        Assert.Equal(2, await db.Properties.CountAsync());
    }

    [Fact]
    public async Task UpdateProperty_ThrowsConflict_WhenChangedToMatchAnotherExistingProperty()
    {
        var (_, controller) = CreateController(Guid.NewGuid());
        await controller.CreateProperty(NewRequest("Suite A"), CancellationToken.None);
        var createdB = await controller.CreateProperty(NewRequest("Suite B"), CancellationToken.None);
        var propertyBId = Assert.IsType<PropertyResponse>(Assert.IsType<CreatedAtActionResult>(createdB).Value).Id;

        await Assert.ThrowsAsync<ConflictException>(
            () => controller.UpdateProperty(propertyBId, NewRequest("Suite A"), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateProperty_AllowsSavingItsOwnUnchangedAddressAndUnitIdentifier()
    {
        var (_, controller) = CreateController(Guid.NewGuid());
        var created = await controller.CreateProperty(NewRequest("Suite A"), CancellationToken.None);
        var propertyId = Assert.IsType<PropertyResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.UpdateProperty(
            propertyId, NewRequest("Suite A") with { TargetRent = 1500m }, CancellationToken.None);

        var response = Assert.IsType<PropertyResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(1500m, response.TargetRent);
    }

    [Fact]
    public async Task CreateProperty_ThrowsValidationException_WhenUnitIdentifierExceedsMaxLength()
    {
        var (_, controller) = CreateController(Guid.NewGuid());
        var request = NewRequest(new string('A', 51));

        await Assert.ThrowsAsync<ValidationException>(
            () => controller.CreateProperty(request, CancellationToken.None));
    }

    [Fact]
    public async Task GetProperty_ThrowsNotFound_ForAnotherTenantsProperty()
    {
        var (_, controllerA) = CreateController(Guid.NewGuid());
        var created = await controllerA.CreateProperty(NewRequest(), CancellationToken.None);
        var propertyId = Assert.IsType<PropertyResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var (_, controllerB) = CreateController(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(
            () => controllerB.GetProperty(propertyId, CancellationToken.None));
    }

    [Fact]
    public async Task GetProperties_ReturnsOnlyActiveTenantsProperties()
    {
        var tenantId = Guid.NewGuid();
        var (_, controllerA) = CreateController(tenantId);
        await controllerA.CreateProperty(NewRequest("Suite A"), CancellationToken.None);

        var (_, controllerOtherTenant) = CreateController(Guid.NewGuid());
        await controllerOtherTenant.CreateProperty(NewRequest(), CancellationToken.None);

        var (_, controllerB) = CreateController(tenantId);
        var result = await controllerB.GetProperties(null, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PropertyListResponse>(ok.Value);

        Assert.Equal(1, response.TotalCount);
        var item = Assert.Single(response.Items);
        Assert.Equal("Suite A", item.UnitIdentifier);
    }

    [Fact]
    public async Task GetProperties_WithPageSize_PaginatesAndReportsTotalCount()
    {
        var tenantId = Guid.NewGuid();
        var (_, controller) = CreateController(tenantId);
        for (var i = 0; i < 3; i++)
        {
            // Distinct UnitIdentifier per row -- CreateProperty now rejects an exact
            // duplicate (same address + unit identifier) as a real Conflict, so three
            // properties sharing an address need to actually look like three real,
            // independent suites, not three copies of the same one.
            await controller.CreateProperty(
                NewRequest($"Unit {i}") with { Name = $"Property {i}" }, CancellationToken.None);
        }

        var (_, pageController) = CreateController(tenantId);
        var result = await pageController.GetProperties(pageNumber: 2, pageSize: 2, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PropertyListResponse>(ok.Value);

        Assert.Equal(3, response.TotalCount);
        Assert.Equal(2, response.PageNumber);
        Assert.Equal(2, response.PageSize);
        Assert.Single(response.Items);
    }

    [Fact]
    public async Task DeleteProperty_TodayAlwaysHardDeletes()
    {
        // HasAppliedPaymentsAsync is a placeholder that always returns false until Phase 1
        // ships a real payment ledger (see the Sprint 3 doc's Executive Summary) -- every
        // delete today takes the hard-delete branch.
        var (db, controller) = CreateController(Guid.NewGuid());
        var created = await controller.CreateProperty(NewRequest(), CancellationToken.None);
        var propertyId = Assert.IsType<PropertyResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.DeleteProperty(propertyId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(0, await db.Properties.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task DeleteProperty_ThrowsNotFound_ForAnotherTenantsProperty()
    {
        var (_, controllerA) = CreateController(Guid.NewGuid());
        var created = await controllerA.CreateProperty(NewRequest(), CancellationToken.None);
        var propertyId = Assert.IsType<PropertyResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var (_, controllerB) = CreateController(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(
            () => controllerB.DeleteProperty(propertyId, CancellationToken.None));
    }

    private static IFormFile CreateCsvFormFile(string content, string fileName = "properties.csv")
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv",
        };
    }

    [Fact]
    public async Task ImportProperties_ValidFile_CreatesOneFlatPropertyPerRow()
    {
        const string csv = """
            PropertyName,PropertyType,StreetAddress1,City,State,PostalCode,Country,UnitIdentifier,TargetRent
            Riverside Apartments,MultiFamily,100 Main St,Provo,UT,84601,USA,Suite A,1200
            Riverside Apartments,MultiFamily,100 Main St,Provo,UT,84601,USA,Suite B,1250
            Lone Peak House,SingleFamily,5 Center St,Ogden,UT,84401,USA,,
            """;

        var (db, controller) = CreateController(Guid.NewGuid());
        var result = await controller.ImportProperties(CreateCsvFormFile(csv), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ImportPropertiesResponse>(ok.Value);

        Assert.True(response.Success);
        Assert.Equal(3, response.TotalRows);
        Assert.Equal(0, response.InvalidRowCount);
        Assert.Equal(3, response.PropertiesCreated);

        Assert.Equal(3, await db.Properties.CountAsync());
        var standalone = await db.Properties.SingleAsync(p => p.Name == "Lone Peak House");
        Assert.Null(standalone.UnitIdentifier);
    }

    [Fact]
    public async Task ImportProperties_TwoRowsWithSameAddressAndUnitIdentifier_RejectsTheDuplicateRow()
    {
        const string csv = """
            PropertyName,PropertyType,StreetAddress1,City,State,PostalCode,Country,UnitIdentifier,TargetRent
            Riverside Apartments,MultiFamily,100 Main St,Provo,UT,84601,USA,Suite A,1200
            Riverside Apartments,MultiFamily,100 Main St,Provo,UT,84601,USA,Suite A,1200
            """;

        var (db, controller) = CreateController(Guid.NewGuid());
        var result = await controller.ImportProperties(CreateCsvFormFile(csv), CancellationToken.None);

        var response = Assert.IsType<ImportPropertiesResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.False(response.Success);
        Assert.Equal(1, response.InvalidRowCount);
        Assert.Contains(response.Rows, r => !r.IsValid && r.Errors.Any(e => e.Contains("Duplicate of another row")));
        Assert.Equal(0, await db.Properties.CountAsync());
    }

    [Fact]
    public async Task ImportProperties_RowMatchingAnExistingProperty_RejectsTheRow()
    {
        var (_, controller) = CreateController(Guid.NewGuid());
        await controller.CreateProperty(NewRequest("Suite A"), CancellationToken.None);

        const string csv = """
            PropertyName,PropertyType,StreetAddress1,City,State,PostalCode,Country,UnitIdentifier,TargetRent
            Riverside Apartments,MultiFamily,100 Main St,Provo,UT,84601,USA,Suite A,1200
            """;

        var result = await controller.ImportProperties(CreateCsvFormFile(csv), CancellationToken.None);

        var response = Assert.IsType<ImportPropertiesResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.False(response.Success);
        Assert.Equal(1, response.InvalidRowCount);
        Assert.Contains(response.Rows, r => !r.IsValid && r.Errors.Any(e => e.Contains("already exists")));
    }

    [Fact]
    public async Task ImportProperties_OneInvalidRow_PersistsNothingAtAll()
    {
        const string csv = """
            PropertyName,PropertyType,StreetAddress1,City,State,PostalCode,Country,UnitIdentifier,TargetRent
            Riverside Apartments,MultiFamily,100 Main St,Provo,UT,84601,USA,Suite A,1200
            Riverside Apartments,MultiFamily,100 Main St,Provo,UT,84601,USA,Suite B,not-a-number
            """;

        var (db, controller) = CreateController(Guid.NewGuid());
        var result = await controller.ImportProperties(CreateCsvFormFile(csv), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ImportPropertiesResponse>(ok.Value);

        Assert.False(response.Success);
        Assert.Equal(1, response.InvalidRowCount);
        Assert.Equal(0, response.PropertiesCreated);
        Assert.Contains(response.Rows, r => r.RowNumber == 3 && !r.IsValid && r.Errors.Contains("Target Rent must be a positive number."));

        Assert.Equal(0, await db.Properties.CountAsync());
    }

    [Fact]
    public async Task ImportProperties_SanitizesFormulaInjectionAndHtmlInTextFields()
    {
        const string csv = """
            PropertyName,PropertyType,StreetAddress1,City,State,PostalCode,Country,UnitIdentifier,TargetRent
            =cmd|'/c calc'!A1,MultiFamily,100 Main St,Provo,UT,84601,USA,<script>alert(1)</script>Suite A,1200
            """;

        var (db, controller) = CreateController(Guid.NewGuid());
        await controller.ImportProperties(CreateCsvFormFile(csv), CancellationToken.None);

        var property = await db.Properties.SingleAsync();
        Assert.StartsWith("'=", property.Name);
        Assert.DoesNotContain('<', property.UnitIdentifier!);
    }

    [Fact]
    public async Task ImportProperties_RejectsUnsupportedFileExtension()
    {
        var (_, controller) = CreateController(Guid.NewGuid());

        await Assert.ThrowsAsync<ValidationException>(
            () => controller.ImportProperties(CreateCsvFormFile("irrelevant", "properties.txt"), CancellationToken.None));
    }
}
