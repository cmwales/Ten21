using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Properties;
using Ten21.Api.Controllers;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
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

        var options = new DbContextOptionsBuilder<Ten21DbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(tenantContext))
            .Options;
        var db = new Ten21DbContext(options, tenantContext);
        db.Database.EnsureCreated();

        return (db, new PropertiesController(db, _sanitizer));
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
}
