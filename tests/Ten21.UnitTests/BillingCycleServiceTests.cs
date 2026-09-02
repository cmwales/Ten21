using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Business.Billing;
using Ten21.Business.Charges;
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

    private BillingCycleService CreateService(Ten21DbContext db)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(_tenantId);
        return new BillingCycleService(db, new ChargeRepository(db), tenantContext);
    }

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
        var service = CreateService(db);

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
        var service = CreateService(db);

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
        var service = CreateService(db);

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
        var service = CreateService(db);

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
        var service = CreateService(db);

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
        var service = CreateService(db);

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
        var service = CreateService(db);

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
        var service = CreateService(db);

        var result = await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(2, result.ChargesGenerated);
        Assert.Equal(2, await db.Charges.CountAsync());
    }

    // US-45: late fee assessment.

    private Charge NewOverdueBaseRentCharge(DateOnly dueDate, decimal amount = 1450m) => new()
    {
        Id = Guid.NewGuid(),
        PropertyId = _propertyId,
        Description = "Base Rent",
        Amount = amount,
        DueDate = dueDate,
        Category = ChargeCategory.BaseRent,
        AllocationPriority = Charge.DefaultAllocationPriorityFor(ChargeCategory.BaseRent),
        Status = ChargeLifecycleStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private LateFeePolicy NewPolicy(
        LateFeePolicyType type, int gracePeriodDays = 5, decimal? baseAmount = null,
        decimal? percentageRate = null, decimal? dailyAccrualRate = null, decimal? maxFeeCap = null) => new()
    {
        Id = Guid.NewGuid(),
        LeaseId = _leaseId,
        GracePeriodDays = gracePeriodDays,
        PolicyType = type,
        BaseAmount = baseAmount,
        PercentageRate = percentageRate,
        DailyAccrualRate = dailyAccrualRate,
        MaxFeeCap = maxFeeCap,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task RunCycleAsync_AssessesALateFee_WhenBaseRentIsPastItsGracePeriod()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        await SeedPropertyAndLeaseAsync(db, today.AddYears(1));
        db.LateFeePolicies.Add(NewPolicy(LateFeePolicyType.Flat, gracePeriodDays: 5, baseAmount: 50m));
        db.Charges.Add(NewOverdueBaseRentCharge(today.AddDays(-10)));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(1, result.LateFeesAssessed);
        var lateFee = Assert.Single(await db.Charges.Where(c => c.Category == ChargeCategory.LateFee).ToListAsync());
        Assert.Equal(50m, lateFee.Amount);
        Assert.Equal(Charge.DefaultAllocationPriorityFor(ChargeCategory.LateFee), lateFee.AllocationPriority);
    }

    [Fact]
    public async Task RunCycleAsync_DoesNotAssessALateFee_WithinTheGracePeriod()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        await SeedPropertyAndLeaseAsync(db, today.AddYears(1));
        db.LateFeePolicies.Add(NewPolicy(LateFeePolicyType.Flat, gracePeriodDays: 5, baseAmount: 50m));
        db.Charges.Add(NewOverdueBaseRentCharge(today.AddDays(-3))); // 3 days overdue, grace is 5
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(0, result.LateFeesAssessed);
        Assert.Empty(await db.Charges.Where(c => c.Category == ChargeCategory.LateFee).ToListAsync());
    }

    [Fact]
    public async Task RunCycleAsync_DoesNotReassessAFlatLateFee_OnASecondRunForTheSameOverdueCharge()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        await SeedPropertyAndLeaseAsync(db, today.AddYears(1));
        db.LateFeePolicies.Add(NewPolicy(LateFeePolicyType.Flat, gracePeriodDays: 5, baseAmount: 50m));
        db.Charges.Add(NewOverdueBaseRentCharge(today.AddDays(-10)));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await service.RunCycleAsync(CancellationToken.None);
        var secondRun = await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(0, secondRun.LateFeesAssessed);
        Assert.Single(await db.Charges.Where(c => c.Category == ChargeCategory.LateFee).ToListAsync());
    }

    [Fact]
    public async Task RunCycleAsync_AssessesADailyAccruingLateFee_OnEveryRunWhileStillOverdue()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        await SeedPropertyAndLeaseAsync(db, today.AddYears(1));
        db.LateFeePolicies.Add(NewPolicy(LateFeePolicyType.DailyAccruing, gracePeriodDays: 5, dailyAccrualRate: 10m));
        db.Charges.Add(NewOverdueBaseRentCharge(today.AddDays(-10)));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        // Same executionDate both times in this unit test (both calls happen "today"), so a
        // naive re-run would be blocked by the (PropertyId, LateFee, DueDate) idempotency key
        // exactly like Flat -- this proves same-day re-runs are still safe, not that two
        // different calendar days each accrue their own increment (see the DueDate=executionDate
        // design note on AssessLateFeesAsync for why the latter holds on real subsequent days).
        await service.RunCycleAsync(CancellationToken.None);
        var secondRun = await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(0, secondRun.LateFeesAssessed);
        Assert.Single(await db.Charges.Where(c => c.Category == ChargeCategory.LateFee).ToListAsync());
    }

    [Fact]
    public async Task RunCycleAsync_CapsCumulativeLateFees_AtMaxFeeCap()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        await SeedPropertyAndLeaseAsync(db, today.AddYears(1));
        db.LateFeePolicies.Add(NewPolicy(LateFeePolicyType.Flat, gracePeriodDays: 5, baseAmount: 50m, maxFeeCap: 30m));
        db.Charges.Add(NewOverdueBaseRentCharge(today.AddDays(-10)));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(1, result.LateFeesAssessed);
        var lateFee = Assert.Single(await db.Charges.Where(c => c.Category == ChargeCategory.LateFee).ToListAsync());
        Assert.Equal(30m, lateFee.Amount); // capped below the policy's own 50 BaseAmount
    }

    [Fact]
    public async Task RunCycleAsync_SkipsLateFeeAssessment_WhenTheOverdueBalanceIsAlreadyPaid()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        await SeedPropertyAndLeaseAsync(db, today.AddYears(1));
        db.LateFeePolicies.Add(NewPolicy(LateFeePolicyType.Flat, gracePeriodDays: 5, baseAmount: 50m));
        var charge = NewOverdueBaseRentCharge(today.AddDays(-10));
        db.Charges.Add(charge);
        await db.SaveChangesAsync();
        // A full-amount credit adjustment zeroes Outstanding the same way a payment
        // allocation would (see ChargeLedgerMath.Outstanding) -- avoids needing a real
        // PaymentTransaction/ResidentProfile FK chain just to prove "already resolved."
        db.ChargeAdjustments.Add(new ChargeAdjustment
        {
            Id = Guid.NewGuid(),
            TargetChargeId = charge.Id,
            AdjustmentType = AdjustmentType.CreditAdjustment,
            Amount = charge.Amount,
            Reason = "Paid in full",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.RunCycleAsync(CancellationToken.None);

        Assert.Equal(0, result.LateFeesAssessed);
    }

    [Fact]
    public async Task RunCycleAsync_RollsBackEverything_WhenLateFeeAssessmentFailsAfterChargeGenerationSucceeded()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
        using var db = CreateDbContext();
        await SeedPropertyAndLeaseAsync(db, today.AddYears(1));
        // A normal, valid template -- GenerateRecurringChargesAsync (step 1) succeeds and
        // stages a real Charge from this.
        db.LeaseRecurringCharges.Add(NewTemplate(today.AddMonths(-1), today.Day));
        db.Charges.Add(NewOverdueBaseRentCharge(today.AddDays(-10)));
        // An invalid PolicyType forces AssessLateFeesAsync (step 2, runs after step 1) to
        // throw -- proving step 1's already-staged charge rolls back too, not just step 2's
        // own work, since the whole cycle is one transaction.
        db.LateFeePolicies.Add(NewPolicy((LateFeePolicyType)999, gracePeriodDays: 5, baseAmount: 50m));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.RunCycleAsync(CancellationToken.None));

        // Only the pre-existing seeded overdue charge remains -- step 1's freshly-generated
        // BaseRent charge was rolled back along with everything else.
        Assert.Equal(1, await db.Charges.CountAsync());
    }
}
