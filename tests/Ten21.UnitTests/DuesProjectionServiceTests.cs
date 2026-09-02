using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Business.Billing;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-47 (Sprint 9): the read-time 30-day projection -- pure query, no writes.
/// Deliberately not using BillingCycleService's own SeedPropertyAndLeaseAsync-style helper
/// so this test suite doesn't accidentally depend on generation-engine behavior.</summary>
public class DuesProjectionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _propertyId = Guid.NewGuid();
    private readonly Guid _leaseId = Guid.NewGuid();

    public DuesProjectionServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private Ten21DbContext CreateDbContext()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(_tenantId);
        var options = new DbContextOptionsBuilder<Ten21DbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(tenantContext, new HardDeleteOverride()))
            .Options;
        var db = new Ten21DbContext(options, tenantContext);
        db.Database.EnsureCreated();
        return db;
    }

    private async Task SeedPropertyAndLeaseAsync(Ten21DbContext db)
    {
        db.Properties.Add(new Property
        {
            Id = _propertyId,
            Name = "Test Property",
            PropertyType = PropertyType.MultiFamily,
            StreetAddress1 = "1 Main St",
            City = "Provo",
            State = "UT",
            PostalCode = "84601",
            Country = "USA",
            OccupancyStatus = OccupancyStatus.Occupied,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        var residentId = Guid.NewGuid();
        db.ResidentProfiles.Add(new ResidentProfile
        {
            Id = residentId,
            PropertyId = _propertyId,
            OccupantType = OccupantType.Primary,
            FirstName = "Test",
            LastName = "Resident",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.Leases.Add(new Lease
        {
            Id = _leaseId,
            PropertyId = _propertyId,
            ResidentId = residentId,
            StartDate = new DateOnly(2020, 1, 1),
            EndDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime).AddYears(1),
            Status = LeaseStatus.FixedTerm,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private LeaseRecurringCharge NewTemplate(DateOnly effectiveStartDate, int dueDayOfMonth, bool isPaused = false) => new()
    {
        Id = Guid.NewGuid(),
        LeaseId = _leaseId,
        ChargeName = "Base Rent",
        Category = ChargeCategory.BaseRent,
        Amount = 1450m,
        RecurrencePattern = RecurrencePattern.Monthly,
        RecurrenceInterval = 1,
        DueDayOfMonth = dueDayOfMonth,
        EndStrategy = EndStrategy.Indefinite,
        EffectiveStartDate = effectiveStartDate,
        ProrationStrategy = ProrationStrategy.FullAmount,
        IsPaused = isPaused,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task GetProjectionAsync_IncludesAnOccurrence_WithinTheThirtyDayWindow()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        await SeedPropertyAndLeaseAsync(db);
        // Due 10 days from today, comfortably inside the window.
        var dueDate = today.AddDays(10);
        db.LeaseRecurringCharges.Add(NewTemplate(today.AddMonths(-1), dueDate.Day));
        await db.SaveChangesAsync();
        var service = new DuesProjectionService(db);

        var projection = await service.GetProjectionAsync(_propertyId, CancellationToken.None);

        Assert.Contains(projection, p => p.DueDate == dueDate && p.Amount == 1450m && p.Category == ChargeCategory.BaseRent);
    }

    [Fact]
    public async Task GetProjectionAsync_ExcludesAPausedTemplate()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        await SeedPropertyAndLeaseAsync(db);
        db.LeaseRecurringCharges.Add(NewTemplate(today.AddMonths(-1), today.AddDays(10).Day, isPaused: true));
        await db.SaveChangesAsync();
        var service = new DuesProjectionService(db);

        var projection = await service.GetProjectionAsync(_propertyId, CancellationToken.None);

        Assert.Empty(projection);
    }

    [Fact]
    public async Task GetProjectionAsync_HasNoWriteSideEffects()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        await SeedPropertyAndLeaseAsync(db);
        db.LeaseRecurringCharges.Add(NewTemplate(today.AddMonths(-1), today.AddDays(5).Day));
        await db.SaveChangesAsync();
        var service = new DuesProjectionService(db);

        await service.GetProjectionAsync(_propertyId, CancellationToken.None);

        Assert.Empty(await db.Charges.ToListAsync());
    }

    [Fact]
    public async Task GetProjectionAsync_ThrowsNotFound_WhenThePropertyDoesNotExist()
    {
        using var db = CreateDbContext();
        var service = new DuesProjectionService(db);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetProjectionAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
