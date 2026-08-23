using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Properties;
using Ten21.Api.Controllers;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Application.Abstractions;
using Ten21.Infrastructure.Import;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Ten21.Infrastructure.Security;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-19: Property/Unit create + update, including tenant isolation on lookups,
/// input sanitization, and Unit reconciliation on update (add/edit/remove-as-soft-delete).
/// Same in-memory SQLite pattern as TenantIsolationTests/AuditSaveChangesInterceptorTests.
/// </summary>
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

    private static UpsertPropertyRequest NewRequest(params UnitRequest[] units) => new(
        Name: "Riverside Apartments",
        PropertyType: PropertyType.MultiFamily,
        StreetAddress1: "100 Main St",
        StreetAddress2: null,
        City: "Provo",
        State: "UT",
        PostalCode: "84601",
        Country: "USA",
        DefaultTargetRent: 1200m,
        Units: units);

    [Fact]
    public async Task CreateProperty_PersistsPropertyAndCascadesDefaultTargetRentToUnits()
    {
        var (db, controller) = CreateController(Guid.NewGuid());

        var request = NewRequest(
            new UnitRequest(null, "101", null, OccupancyStatus.Vacant),
            new UnitRequest(null, "102", 1500m, OccupancyStatus.Occupied));

        var result = await controller.CreateProperty(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<PropertyResponse>(created.Value);

        Assert.Equal(2, response.Units.Count);
        Assert.Equal(1200m, response.Units.Single(u => u.UnitIdentifier == "101").TargetRent);
        Assert.Equal(1500m, response.Units.Single(u => u.UnitIdentifier == "102").TargetRent);
        Assert.Equal(1, await db.Properties.CountAsync());
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
    public async Task GetProperty_ThrowsNotFound_ForAnotherTenantsProperty()
    {
        var (dbA, controllerA) = CreateController(Guid.NewGuid());
        var createResult = await controllerA.CreateProperty(NewRequest(), CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(createResult);
        var propertyId = Assert.IsType<PropertyResponse>(created.Value).Id;

        var (_, controllerB) = CreateController(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(
            () => controllerB.GetProperty(propertyId, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateProperty_ReconcilesUnits_AddsEditsAndSoftDeletes()
    {
        // A fresh DbContext per operation, same as a real request-scoped DbContext would
        // be -- reusing one context instance across two "requests" (as the other tests in
        // this class do, for brevity) hits EF Core change-tracking edge cases around
        // navigation-collection reconciliation that a real request never encounters.
        var tenantId = Guid.NewGuid();
        var (_, controller) = CreateController(tenantId);

        var createResult = await controller.CreateProperty(
            NewRequest(
                new UnitRequest(null, "101", null, OccupancyStatus.Vacant),
                new UnitRequest(null, "102", null, OccupancyStatus.Vacant)),
            CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(createResult);
        var property = Assert.IsType<PropertyResponse>(created.Value);
        var keptUnitId = property.Units.Single(u => u.UnitIdentifier == "101").Id;

        // Drop unit 102, edit unit 101, add a brand-new unit 103.
        var updateRequest = NewRequest(
            new UnitRequest(keptUnitId, "101-Updated", 999m, OccupancyStatus.Occupied),
            new UnitRequest(null, "103", null, OccupancyStatus.Maintenance)) with { Name = "Riverside Renamed" };

        var (db, updateController) = CreateController(tenantId);
        var updateResult = await updateController.UpdateProperty(property.Id, updateRequest, CancellationToken.None);
        var updated = Assert.IsType<OkObjectResult>(updateResult);
        var response = Assert.IsType<PropertyResponse>(updated.Value);

        Assert.Equal("Riverside Renamed", response.Name);
        Assert.Equal(2, response.Units.Count);
        Assert.Contains(response.Units, u => u.UnitIdentifier == "101-Updated" && u.TargetRent == 999m);
        Assert.Contains(response.Units, u => u.UnitIdentifier == "103");
        Assert.DoesNotContain(response.Units, u => u.UnitIdentifier == "102");

        // The dropped unit is soft-deleted (AuditSaveChangesInterceptor), not hard-deleted.
        var softDeletedUnit = await db.Units.IgnoreQueryFilters()
            .SingleAsync(u => u.UnitIdentifier == "102");
        Assert.True(softDeletedUnit.IsDeleted);
    }

    [Fact]
    public async Task GetProperties_ReturnsOnlyActiveTenantsProperties_WithNestedUnits()
    {
        var tenantId = Guid.NewGuid();
        var (_, controllerA) = CreateController(tenantId);
        await controllerA.CreateProperty(
            NewRequest(new UnitRequest(null, "101", null, OccupancyStatus.Vacant)), CancellationToken.None);

        var (_, controllerOtherTenant) = CreateController(Guid.NewGuid());
        await controllerOtherTenant.CreateProperty(NewRequest(), CancellationToken.None);

        var (_, controllerB) = CreateController(tenantId);
        var result = await controllerB.GetProperties(null, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PropertyListResponse>(ok.Value);

        Assert.Equal(1, response.TotalCount);
        var item = Assert.Single(response.Items);
        Assert.Equal("Riverside Apartments", item.Name);
        var unit = Assert.Single(item.Units);
        Assert.Equal("101", unit.UnitIdentifier);
    }

    [Fact]
    public async Task GetProperties_WithPageSize_PaginatesAndReportsTotalPropertyCount()
    {
        var tenantId = Guid.NewGuid();
        var (_, controller) = CreateController(tenantId);
        for (var i = 0; i < 3; i++)
        {
            await controller.CreateProperty(NewRequest() with { Name = $"Property {i}" }, CancellationToken.None);
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
    public async Task ImportProperties_ValidFile_GroupsRowsByPropertyAndCommitsInOneBatch()
    {
        const string csv = """
            PropertyName,PropertyType,StreetAddress1,City,State,PostalCode,Country,UnitIdentifier,TargetRent
            Riverside Apartments,MultiFamily,100 Main St,Provo,UT,84601,USA,101,1200
            Riverside Apartments,MultiFamily,100 Main St,Provo,UT,84601,USA,102,1250
            Downtown Lofts,Commercial,5 Center St,Ogden,UT,84401,USA,A,
            """;

        var (db, controller) = CreateController(Guid.NewGuid());
        var result = await controller.ImportProperties(CreateCsvFormFile(csv), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ImportPropertiesResponse>(ok.Value);

        Assert.True(response.Success);
        Assert.Equal(3, response.TotalRows);
        Assert.Equal(0, response.InvalidRowCount);
        Assert.Equal(2, response.PropertiesCreated);
        Assert.Equal(3, response.UnitsCreated);

        Assert.Equal(2, await db.Properties.CountAsync());
        Assert.Equal(3, await db.Units.CountAsync());

        var riverside = await db.Properties.Include(p => p.Units).SingleAsync(p => p.Name == "Riverside Apartments");
        Assert.Equal(2, riverside.Units.Count);
    }

    [Fact]
    public async Task ImportProperties_OneInvalidRow_PersistsNothingAtAll()
    {
        const string csv = """
            PropertyName,PropertyType,StreetAddress1,City,State,PostalCode,Country,UnitIdentifier,TargetRent
            Riverside Apartments,MultiFamily,100 Main St,Provo,UT,84601,USA,101,1200
            Riverside Apartments,MultiFamily,100 Main St,Provo,UT,84601,USA,102,not-a-number
            """;

        var (db, controller) = CreateController(Guid.NewGuid());
        var result = await controller.ImportProperties(CreateCsvFormFile(csv), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ImportPropertiesResponse>(ok.Value);

        Assert.False(response.Success);
        Assert.Equal(1, response.InvalidRowCount);
        Assert.Equal(0, response.PropertiesCreated);
        Assert.Contains(response.Rows, r => r.RowNumber == 3 && !r.IsValid && r.Errors.Contains("Target Rent must be a positive number."));
        Assert.Contains(response.Rows, r => r.RowNumber == 2 && r.IsValid);

        // The whole batch is rejected, including the otherwise-valid row 2 -- nothing is
        // written to the database at all.
        Assert.Equal(0, await db.Properties.CountAsync());
        Assert.Equal(0, await db.Units.CountAsync());
    }

    [Fact]
    public async Task ImportProperties_SanitizesFormulaInjectionAndHtmlInTextFields()
    {
        const string csv = """
            PropertyName,PropertyType,StreetAddress1,City,State,PostalCode,Country,UnitIdentifier,TargetRent
            =cmd|'/c calc'!A1,MultiFamily,100 Main St,Provo,UT,84601,USA,<script>alert(1)</script>101,1200
            """;

        var (db, controller) = CreateController(Guid.NewGuid());
        await controller.ImportProperties(CreateCsvFormFile(csv), CancellationToken.None);

        var property = await db.Properties.Include(p => p.Units).SingleAsync();
        Assert.StartsWith("'=", property.Name);
        Assert.DoesNotContain('<', property.Units.Single().UnitIdentifier);
    }

    [Fact]
    public async Task ImportProperties_RejectsUnsupportedFileExtension()
    {
        var (_, controller) = CreateController(Guid.NewGuid());

        await Assert.ThrowsAsync<ValidationException>(
            () => controller.ImportProperties(CreateCsvFormFile("irrelevant", "properties.txt"), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteProperty_TodayAlwaysHardDeletes_RemovingBothPropertyAndItsUnits()
    {
        // HasAppliedPaymentsAsync is a placeholder that always returns false until Phase 1
        // ships a real payment ledger (see the Sprint 3 doc's Executive Summary) -- every
        // delete today takes the hard-delete branch. The soft-delete + cascade branch is
        // covered directly at the interceptor level instead, since it's genuinely
        // unreachable through this controller action today by design; see
        // AuditSaveChangesInterceptorTests.SoftDelete_OfProperty_CascadesToChildUnits.
        var (db, controller) = CreateController(Guid.NewGuid());
        var created = await controller.CreateProperty(
            NewRequest(new UnitRequest(null, "101", null, OccupancyStatus.Vacant)), CancellationToken.None);
        var propertyId = Assert.IsType<PropertyResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.DeleteProperty(propertyId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(0, await db.Properties.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.Units.IgnoreQueryFilters().CountAsync());
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
}
