using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.ManualCharges;
using Ten21.Api.Controllers;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Ten21.Infrastructure.Security;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-31: one-time manual charges/fines, nested under a Property the same way
/// LeasesController is. Same in-memory SQLite pattern as LeasesControllerTests.
/// Post-Sprint-6 fix: no more per-resident "bill to" (charges are billed to the unit), and
/// PaidDate is now trackable.</summary>
public class ManualChargesControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HtmlInputSanitizer _sanitizer = new();

    public ManualChargesControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, ManualChargesController Controller) CreateController(Guid tenantId)
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

        return (db, new ManualChargesController(db, _sanitizer));
    }

    private static async Task<Property> SeedPropertyAsync(Ten21DbContext db)
    {
        var property = new Property
        {
            Id = Guid.NewGuid(),
            Name = "Riverside Apartments",
            PropertyType = PropertyType.MultiFamily,
            StreetAddress1 = "100 Main St",
            City = "Provo",
            State = "UT",
            PostalCode = "84601",
            Country = "USA",
            OccupancyStatus = OccupancyStatus.Occupied,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Properties.Add(property);
        await db.SaveChangesAsync();
        return property;
    }

    private static UpsertManualChargeRequest NewRequest(DateOnly? paidDate = null) => new(
        Description: "Trash Violation Fine",
        Amount: 75m,
        DueDate: new DateOnly(2026, 9, 15),
        AccountingCode: "GL-4100",
        PaidDate: paidDate);

    [Fact]
    public async Task CreateManualCharge_Persists_BilledToTheUnit()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);

        var result = await controller.CreateManualCharge(property.Id, NewRequest(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<ManualChargeResponse>(created.Value);
        Assert.Equal(property.Id, response.PropertyId);
        Assert.Equal(75m, response.Amount);
        Assert.Null(response.PaidDate);
        Assert.Equal(1, await db.ManualCharges.CountAsync());
    }

    [Fact]
    public async Task CreateManualCharge_ThrowsValidationException_WhenDescriptionIsMissing()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var request = NewRequest() with { Description = "" };

        await Assert.ThrowsAsync<ValidationException>(() => controller.CreateManualCharge(
            property.Id, request, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task CreateManualCharge_ThrowsValidationException_WhenAmountIsNotPositive(decimal amount)
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var request = NewRequest() with { Amount = amount };

        await Assert.ThrowsAsync<ValidationException>(() => controller.CreateManualCharge(
            property.Id, request, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateManualCharge_UpdatesFieldsDirectly()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateManualCharge(property.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ManualChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.UpdateManualCharge(
            property.Id, id, NewRequest() with { Amount = 100m }, CancellationToken.None);

        var response = Assert.IsType<ManualChargeResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(100m, response.Amount);
    }

    [Fact]
    public async Task UpdateManualCharge_CanRecordAPaidDateDifferentFromToday()
    {
        // Tester scenario: paid by check/cash on Monday, entered into the system on Friday --
        // PaidDate must be settable to the actual payment date, not implicitly "now".
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateManualCharge(property.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ManualChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;
        var monday = new DateOnly(2026, 9, 14);

        var result = await controller.UpdateManualCharge(
            property.Id, id, NewRequest() with { PaidDate = monday }, CancellationToken.None);

        var response = Assert.IsType<ManualChargeResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(monday, response.PaidDate);
    }

    [Fact]
    public async Task DeleteManualCharge_SoftDeletes()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateManualCharge(property.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ManualChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.DeleteManualCharge(property.Id, id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(0, await db.ManualCharges.CountAsync());
        Assert.Equal(1, await db.ManualCharges.IgnoreQueryFilters().CountAsync(c => c.IsDeleted));
    }

    [Fact]
    public async Task GetManualCharges_ReturnsOnlyThisPropertysCharges()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var propertyA = await SeedPropertyAsync(db);
        var propertyB = await SeedPropertyAsync(db);
        await controller.CreateManualCharge(propertyA.Id, NewRequest(), CancellationToken.None);
        await controller.CreateManualCharge(propertyB.Id, NewRequest(), CancellationToken.None);

        var result = await controller.GetManualCharges(propertyA.Id, CancellationToken.None);

        var charges = Assert.IsAssignableFrom<IReadOnlyList<ManualChargeResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        var charge = Assert.Single(charges);
        Assert.Equal(propertyA.Id, charge.PropertyId);
    }

    [Fact]
    public async Task GetManualCharge_ThrowsNotFound_WhenChargeBelongsToADifferentProperty()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var propertyA = await SeedPropertyAsync(db);
        var propertyB = await SeedPropertyAsync(db);
        var created = await controller.CreateManualCharge(propertyA.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ManualChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        await Assert.ThrowsAsync<NotFoundException>(() => controller.GetManualCharge(propertyB.Id, id, CancellationToken.None));
    }
}
