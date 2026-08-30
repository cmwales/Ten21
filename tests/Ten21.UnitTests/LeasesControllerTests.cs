using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Leases;
using Ten21.Api.Controllers;
using Ten21.Business.Charges;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Ten21.Infrastructure.Security;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-30: Lease + LeaseRecurringCharge CRUD, nested under a Property the same way
/// ResidentsController is. Same in-memory SQLite pattern as PropertiesControllerTests.</summary>
public class LeasesControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HtmlInputSanitizer _sanitizer = new();

    public LeasesControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, LeasesController Controller) CreateController(Guid tenantId)
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

        var authorizationService = TestAuthorizationService.Create(tenantContext);
        var controller = new LeasesController(db, _sanitizer, authorizationService)
        {
            ControllerContext = TestControllerContext.Create(),
        };
        return (db, controller);
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

    private static async Task<ResidentProfile> SeedResidentAsync(Ten21DbContext db, Guid propertyId)
    {
        var resident = new ResidentProfile
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            OccupantType = OccupantType.Primary,
            FirstName = "Dana",
            LastName = "Demo",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ResidentProfiles.Add(resident);
        await db.SaveChangesAsync();
        return resident;
    }

    private static UpsertLeaseRequest NewRequest(Guid residentId, IReadOnlyList<LeaseRecurringChargeRequest>? charges = null) => new(
        ResidentId: residentId,
        StartDate: new DateOnly(2026, 9, 1),
        EndDate: new DateOnly(2027, 8, 31),
        MonthlyBaseRent: 1450m,
        DueDayOfMonth: 1,
        RecurringCharges: charges ?? []);

    [Fact]
    public async Task CreateLease_Persists_AndComputesTotalMonthlyDues()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var request = NewRequest(resident.Id, [new LeaseRecurringChargeRequest("Pet Rent", 50m, "GL-4030")]);

        var result = await controller.CreateLease(property.Id, request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<LeaseResponse>(created.Value);
        Assert.Equal(1500m, response.TotalMonthlyDues);
        Assert.Equal(LeaseStatus.FixedTerm, response.Status);
        Assert.Single(response.RecurringCharges);
        Assert.Equal(1, await db.Leases.CountAsync());
    }

    [Fact]
    public async Task CreateLease_ThrowsNotFound_WhenResidentBelongsToADifferentProperty()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var propertyA = await SeedPropertyAsync(db);
        var propertyB = await SeedPropertyAsync(db);
        var residentOfB = await SeedResidentAsync(db, propertyB.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => controller.CreateLease(
            propertyA.Id, NewRequest(residentOfB.Id), CancellationToken.None));
    }

    [Fact]
    public async Task CreateLease_ThrowsValidationException_WhenEndDateIsNotAfterStartDate()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var request = NewRequest(resident.Id) with { EndDate = new DateOnly(2026, 9, 1) };

        await Assert.ThrowsAsync<ValidationException>(() => controller.CreateLease(
            property.Id, request, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    public async Task CreateLease_ThrowsValidationException_WhenDueDayOfMonthIsOutOfRange(int dueDay)
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var request = NewRequest(resident.Id) with { DueDayOfMonth = dueDay };

        await Assert.ThrowsAsync<ValidationException>(() => controller.CreateLease(
            property.Id, request, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateLease_ReplacesRecurringCharges_WithTheFullGivenSet()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var created = await controller.CreateLease(
            property.Id, NewRequest(resident.Id, [new LeaseRecurringChargeRequest("Pet Rent", 50m, null)]), CancellationToken.None);
        var leaseId = Assert.IsType<LeaseResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var updateRequest = NewRequest(resident.Id, [
            new LeaseRecurringChargeRequest("Parking #12", 75m, "GL-4030"),
            new LeaseRecurringChargeRequest("Fixed Utility Rub", 40m, null),
        ]);
        var result = await controller.UpdateLease(property.Id, leaseId, updateRequest, CancellationToken.None);

        var response = Assert.IsType<LeaseResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(2, response.RecurringCharges.Count);
        Assert.DoesNotContain(response.RecurringCharges, c => c.ChargeName == "Pet Rent");
        Assert.Equal(1450m + 75m + 40m, response.TotalMonthlyDues);
        Assert.Equal(2, await db.LeaseRecurringCharges.CountAsync());
    }

    [Fact]
    public async Task DeleteLease_SoftDeletes()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var created = await controller.CreateLease(property.Id, NewRequest(resident.Id), CancellationToken.None);
        var leaseId = Assert.IsType<LeaseResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.DeleteLease(property.Id, leaseId, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(0, await db.Leases.CountAsync());
        Assert.Equal(1, await db.Leases.IgnoreQueryFilters().CountAsync(l => l.IsDeleted));
    }

    [Fact]
    public async Task GetLease_ThrowsNotFound_WhenLeaseBelongsToADifferentProperty()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var propertyA = await SeedPropertyAsync(db);
        var propertyB = await SeedPropertyAsync(db);
        var residentOfA = await SeedResidentAsync(db, propertyA.Id);
        var created = await controller.CreateLease(propertyA.Id, NewRequest(residentOfA.Id), CancellationToken.None);
        var leaseId = Assert.IsType<LeaseResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        await Assert.ThrowsAsync<NotFoundException>(() => controller.GetLease(propertyB.Id, leaseId, CancellationToken.None));
    }

    [Fact]
    public async Task GetLeases_ReturnsOnlyThisPropertysLeases()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var propertyA = await SeedPropertyAsync(db);
        var propertyB = await SeedPropertyAsync(db);
        var residentOfA = await SeedResidentAsync(db, propertyA.Id);
        var residentOfB = await SeedResidentAsync(db, propertyB.Id);
        await controller.CreateLease(propertyA.Id, NewRequest(residentOfA.Id), CancellationToken.None);
        await controller.CreateLease(propertyB.Id, NewRequest(residentOfB.Id), CancellationToken.None);

        var result = await controller.GetLeases(propertyA.Id, CancellationToken.None);

        var leases = Assert.IsAssignableFrom<IReadOnlyList<LeaseResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        var lease = Assert.Single(leases);
        Assert.Equal(residentOfA.Id, lease.ResidentId);
    }

    // US-32: pro-rated move-in charges + effective status/expiring-soon computation.

    [Fact]
    public async Task CreateMoveInCharge_SameCalendarMonth_ComputesProratedAmount()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        // DueDayOfMonth 1 -> moving in Aug 25 bills through Aug 31 (7 days of 31).
        var request = NewRequest(resident.Id) with { StartDate = new DateOnly(2026, 8, 1), DueDayOfMonth = 1 };
        var created = await controller.CreateLease(property.Id, request, CancellationToken.None);
        var leaseId = Assert.IsType<LeaseResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.CreateMoveInCharge(
            property.Id, leaseId, new CreateMoveInChargeRequest(new DateOnly(2026, 8, 25)), CancellationToken.None);

        var response = Assert.IsType<ChargeResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(property.Id, response.PropertyId);
        Assert.Equal(ChargeCategory.BaseRent, response.Category);
        // 1450m / 31 days * 7 days = 327.42 (rounded).
        Assert.Equal(Math.Round(1450m / 31 * 7, 2), response.Amount);
        Assert.Contains("Aug 25", response.Description);
        Assert.Equal(1, await db.Charges.CountAsync());
    }

    [Fact]
    public async Task CreateMoveInCharge_DueDayAfterMoveInDay_BillsThroughTheNextMonthsAnchor()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        // DueDayOfMonth 5 -> moving in Aug 25 bills through Sep 4 (11 days), not just to Aug 31.
        var request = NewRequest(resident.Id) with { StartDate = new DateOnly(2026, 8, 1), DueDayOfMonth = 5 };
        var created = await controller.CreateLease(property.Id, request, CancellationToken.None);
        var leaseId = Assert.IsType<LeaseResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.CreateMoveInCharge(
            property.Id, leaseId, new CreateMoveInChargeRequest(new DateOnly(2026, 8, 25)), CancellationToken.None);

        var response = Assert.IsType<ChargeResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(Math.Round(1450m / 31 * 11, 2), response.Amount);
    }

    [Fact]
    public async Task CreateMoveInCharge_ThrowsValidationException_WhenMoveInDateIsBeforeLeaseStart()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var request = NewRequest(resident.Id) with { StartDate = new DateOnly(2026, 9, 1) };
        var created = await controller.CreateLease(property.Id, request, CancellationToken.None);
        var leaseId = Assert.IsType<LeaseResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        await Assert.ThrowsAsync<ValidationException>(() => controller.CreateMoveInCharge(
            property.Id, leaseId, new CreateMoveInChargeRequest(new DateOnly(2026, 8, 1)), CancellationToken.None));
    }

    [Fact]
    public async Task CreateMoveInCharge_ThrowsNotFound_WhenLeaseBelongsToADifferentProperty()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var propertyA = await SeedPropertyAsync(db);
        var propertyB = await SeedPropertyAsync(db);
        var residentOfA = await SeedResidentAsync(db, propertyA.Id);
        var created = await controller.CreateLease(propertyA.Id, NewRequest(residentOfA.Id), CancellationToken.None);
        var leaseId = Assert.IsType<LeaseResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        await Assert.ThrowsAsync<NotFoundException>(() => controller.CreateMoveInCharge(
            propertyB.Id, leaseId, new CreateMoveInChargeRequest(new DateOnly(2026, 9, 1)), CancellationToken.None));
    }

    [Fact]
    public async Task GetLease_EffectiveStatusRollsOverToMonthToMonth_WhenEndDateHasPassedWithNoNotice()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        // Both dates safely in the past relative to "today" so EndDate < today is guaranteed.
        var request = NewRequest(resident.Id) with { StartDate = new DateOnly(2020, 1, 1), EndDate = new DateOnly(2020, 12, 31) };
        var created = await controller.CreateLease(property.Id, request, CancellationToken.None);
        var leaseId = Assert.IsType<LeaseResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.GetLease(property.Id, leaseId, CancellationToken.None);

        var response = Assert.IsType<LeaseResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(LeaseStatus.FixedTerm, response.Status); // stored value untouched
        Assert.Equal(LeaseStatus.MonthToMonth, response.EffectiveStatus); // computed rollover
        Assert.False(response.IsExpiringSoon); // already rolled over, not "expiring soon" anymore
    }

    [Fact]
    public async Task GetLease_DoesNotRollOverToMonthToMonth_WhenThePropertyHasAMoveOutNoticeOnFile()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var request = NewRequest(resident.Id) with
        {
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = new DateOnly(2020, 12, 31),
        };
        var created = await controller.CreateLease(property.Id, request, CancellationToken.None);
        var leaseId = Assert.IsType<LeaseResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        // Post-Sprint-6 fix: the notice lives on Property, not Lease -- set it directly here.
        property.MoveOutNoticeDate = new DateOnly(2020, 11, 1);
        await db.SaveChangesAsync();

        var result = await controller.GetLease(property.Id, leaseId, CancellationToken.None);

        var response = Assert.IsType<LeaseResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(LeaseStatus.FixedTerm, response.EffectiveStatus);
    }

    [Fact]
    public async Task GetLeases_RolloverAppliesUniformlyToEveryLeaseOnTheProperty_RegardlessOfWhichResidentsLeaseItIs()
    {
        // Tester feedback that drove the move: "No one cares if one tenant out of 2 moves
        // out -- they need to know when to find more tenants." A property with two
        // co-occupants, each on their own lease, and no notice on file: BOTH leases should
        // roll over once EndDate passes, since the signal is per-unit, not per-resident.
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var residentA = await SeedResidentAsync(db, property.Id);
        var residentB = await SeedResidentAsync(db, property.Id);
        var pastDatesRequest = NewRequest(residentA.Id) with { StartDate = new DateOnly(2020, 1, 1), EndDate = new DateOnly(2020, 12, 31) };
        await controller.CreateLease(property.Id, pastDatesRequest, CancellationToken.None);
        await controller.CreateLease(property.Id, pastDatesRequest with { ResidentId = residentB.Id }, CancellationToken.None);

        var result = await controller.GetLeases(property.Id, CancellationToken.None);

        var leases = Assert.IsAssignableFrom<IReadOnlyList<LeaseResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(2, leases.Count);
        Assert.All(leases, l => Assert.Equal(LeaseStatus.MonthToMonth, l.EffectiveStatus));
    }

    [Fact]
    public async Task GetLease_IsExpiringSoon_WhenEndDateIsWithinTheThresholdWindow()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var request = NewRequest(resident.Id) with { StartDate = soon.AddYears(-1), EndDate = soon };
        var created = await controller.CreateLease(property.Id, request, CancellationToken.None);
        var leaseId = Assert.IsType<LeaseResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.GetLease(property.Id, leaseId, CancellationToken.None);

        var response = Assert.IsType<LeaseResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(response.IsExpiringSoon);
        Assert.Equal(LeaseStatus.FixedTerm, response.EffectiveStatus);
    }

    [Fact]
    public async Task GetLease_IsNotExpiringSoon_WhenEndDateIsFarInTheFuture()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var farOut = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(2);
        var request = NewRequest(resident.Id) with { StartDate = farOut.AddYears(-3), EndDate = farOut };
        var created = await controller.CreateLease(property.Id, request, CancellationToken.None);
        var leaseId = Assert.IsType<LeaseResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.GetLease(property.Id, leaseId, CancellationToken.None);

        var response = Assert.IsType<LeaseResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.False(response.IsExpiringSoon);
    }
}
