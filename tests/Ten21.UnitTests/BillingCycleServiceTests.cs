using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Business.Billing;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-44 (Sprint 9): end-to-end generation behavior -- idempotency, IsPaused
/// suppression, and the effective-date boundary, all through a real (SQLite in-memory)
/// DbContext so the EF global tenant query filter and the transaction actually run. See
/// RecurrenceScheduleTests for pure date-math coverage of every recurrence pattern.</summary>
public class BillingCycleServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _propertyId = Guid.NewGuid();
    private readonly Guid _leaseId = Guid.NewGuid();

    public BillingCycleServiceTests()
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

    private async Task SeedPropertyAndLeaseAsync(Ten21DbContext db, DateOnly leaseEndDate)
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
            EndDate = leaseEndDate,
            Status = LeaseStatus.FixedTerm,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private LeaseRecurringCharge NewTemplate(
        DateOnly effectiveStartDate,
        int dueDayOfMonth,
        ChargeCategory category = ChargeCategory.BaseRent,
        bool isPaused = false,
        EndStrategy endStrategy = EndStrategy.LeaseAligned,
        DateOnly? effectiveEndDate = null) => new()
    {
        Id = Guid.NewGuid(),
        LeaseId = _leaseId,
        ChargeName = category == ChargeCategory.BaseRent ? "Base Rent" : "Pet Rent",
        Category = category,
        Amount = 1450m,
        RecurrencePattern = RecurrencePattern.Monthly,
        RecurrenceInterval = 1,
        DueDayOfMonth = dueDayOfMonth,
        EndStrategy = endStrategy,
        EffectiveStartDate = effectiveStartDate,
        EffectiveEndDate = effectiveEndDate,
        ProrationStrategy = ProrationStrategy.FullAmount,
        IsPaused = isPaused,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task RunCycleAsync_GeneratesACharge_WhenTemplateIsDueToday()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        await SeedPropertyAndLeaseAsync(db, today.AddYears(1));
        db.LeaseRecurringCharges.Add(NewTemplate(today.AddMonths(-1), today.Day));
        await db.SaveChangesAsync();
        var service = new BillingCycleService(db);

        var result = await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(1, result.ChargesGenerated);
        var charge = Assert.Single(await db.Charges.ToListAsync());
        Assert.Equal(_propertyId, charge.PropertyId);
        Assert.Equal(ChargeCategory.BaseRent, charge.Category);
    }

    [Fact]
    public async Task RunCycleAsync_ServerDerivesAllocationPriority_FromCategory()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        await SeedPropertyAndLeaseAsync(db, today.AddYears(1));
        db.LeaseRecurringCharges.Add(NewTemplate(today.AddMonths(-1), today.Day));
        await db.SaveChangesAsync();
        var service = new BillingCycleService(db);

        await service.RunCycleAsync(CancellationToken.None);

        var charge = Assert.Single(await db.Charges.ToListAsync());
        Assert.Equal(Charge.DefaultAllocationPriorityFor(ChargeCategory.BaseRent), charge.AllocationPriority);
    }

    [Fact]
    public async Task RunCycleAsync_IsIdempotent_WhenRunTwiceOnTheSameDay()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        await SeedPropertyAndLeaseAsync(db, today.AddYears(1));
        db.LeaseRecurringCharges.Add(NewTemplate(today.AddMonths(-1), today.Day));
        await db.SaveChangesAsync();
        var service = new BillingCycleService(db);

        await service.RunCycleAsync(CancellationToken.None);
        var secondRun = await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(0, secondRun.ChargesGenerated);
        Assert.Equal(1, await db.Charges.CountAsync());
    }

    [Fact]
    public async Task RunCycleAsync_SkipsATemplate_WhenIsPaused()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        await SeedPropertyAndLeaseAsync(db, today.AddYears(1));
        db.LeaseRecurringCharges.Add(NewTemplate(today.AddMonths(-1), today.Day, isPaused: true));
        await db.SaveChangesAsync();
        var service = new BillingCycleService(db);

        var result = await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(0, result.ChargesGenerated);
        Assert.Empty(await db.Charges.ToListAsync());
    }

    [Fact]
    public async Task RunCycleAsync_SkipsATemplate_WhenLeaseAlignedEndDateHasPassed()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        // Lease already ended yesterday -- LeaseAligned's dynamic boundary should stop generation.
        await SeedPropertyAndLeaseAsync(db, today.AddDays(-1));
        db.LeaseRecurringCharges.Add(NewTemplate(today.AddMonths(-1), today.Day));
        await db.SaveChangesAsync();
        var service = new BillingCycleService(db);

        var result = await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(0, result.ChargesGenerated);
    }

    [Fact]
    public async Task RunCycleAsync_SkipsATemplate_WhenFixedDateEffectiveEndDateHasPassed()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        await SeedPropertyAndLeaseAsync(db, today.AddYears(1));
        db.LeaseRecurringCharges.Add(NewTemplate(
            today.AddMonths(-1), today.Day, endStrategy: EndStrategy.FixedDate, effectiveEndDate: today.AddDays(-1)));
        await db.SaveChangesAsync();
        var service = new BillingCycleService(db);

        var result = await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(0, result.ChargesGenerated);
    }

    [Fact]
    public async Task RunCycleAsync_DoesNotGenerate_BeforeEffectiveStartDate()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        await SeedPropertyAndLeaseAsync(db, today.AddYears(1));
        db.LeaseRecurringCharges.Add(NewTemplate(today.AddDays(1), today.Day));
        await db.SaveChangesAsync();
        var service = new BillingCycleService(db);

        var result = await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(0, result.ChargesGenerated);
    }

    [Fact]
    public async Task RunCycleAsync_GeneratesOneChargePerActiveTemplate_ForMultipleLeases()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        await SeedPropertyAndLeaseAsync(db, today.AddYears(1));
        db.LeaseRecurringCharges.AddRange(
            NewTemplate(today.AddMonths(-1), today.Day, ChargeCategory.BaseRent),
            NewTemplate(today.AddMonths(-1), today.Day, ChargeCategory.AddOn));
        await db.SaveChangesAsync();
        var service = new BillingCycleService(db);

        var result = await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(2, result.ChargesGenerated);
        Assert.Equal(2, await db.Charges.CountAsync());
    }
}
