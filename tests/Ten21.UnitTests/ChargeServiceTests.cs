using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Business.Charges;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Ten21.Infrastructure.Security;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>
/// Business-layer refactor: exercises ChargeService directly, with no ChargesController/HTTP
/// layer involved at all -- proving the actual point of extracting this logic out of the
/// controller: it's now testable (and reusable) independent of ASP.NET Core. ChargesController
/// itself keeps its full test coverage in ChargesControllerTests.cs (resource authorization,
/// route wiring, response envelopes); this file only re-covers the business rules that moved.
/// </summary>
public class ChargeServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HtmlInputSanitizer _sanitizer = new();

    public ChargeServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, ChargeService Service) CreateService(Guid tenantId)
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

        return (db, new ChargeService(db, new ChargeRepository(db), _sanitizer));
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

    private static UpsertChargeRequest NewRequest(ChargeCategory category = ChargeCategory.LateFee) => new(
        Description: "Trash Violation Fine",
        Amount: 75m,
        DueDate: new DateOnly(2026, 9, 15),
        AccountingCode: "GL-4100",
        Category: category);

    private static async Task AllocatePaymentAsync(Ten21DbContext db, Guid propertyId, Guid chargeId, decimal amount)
    {
        var resident = new ResidentProfile
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            OccupantType = OccupantType.Primary,
            FirstName = "Jamie",
            LastName = "Rivera",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ResidentProfiles.Add(resident);

        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            ResidentProfileId = resident.Id,
            PaymentDate = new DateOnly(2026, 9, 16),
            AmountPaid = amount,
            TenderType = TenderType.Cash,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.PaymentTransactions.Add(payment);
        db.PaymentAllocations.Add(new PaymentAllocation
        {
            Id = Guid.NewGuid(),
            PaymentTransactionId = payment.Id,
            ChargeId = chargeId,
            AllocatedAmount = amount,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateAsync_Persists_UnpaidWithDerivedAllocationPriority()
    {
        var (db, service) = CreateService(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);

        var response = await service.CreateAsync(property.Id, NewRequest(ChargeCategory.BaseRent), CancellationToken.None);

        Assert.Equal(property.Id, response.PropertyId);
        Assert.Equal(ChargePaymentStatus.Unpaid, response.PaymentStatus);
        Assert.False(response.IsLocked);

        var stored = await db.Charges.SingleAsync();
        Assert.Equal(Charge.DefaultAllocationPriorityFor(ChargeCategory.BaseRent), stored.AllocationPriority);
    }

    [Fact]
    public async Task CreateAsync_ThrowsNotFound_WhenPropertyDoesNotExist()
    {
        var (_, service) = CreateService(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.CreateAsync(Guid.NewGuid(), NewRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_ThrowsConflict_WhenChargeIsLocked()
    {
        var (db, service) = CreateService(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var response = await service.CreateAsync(property.Id, NewRequest(), CancellationToken.None);
        await AllocatePaymentAsync(db, property.Id, response.Id, 75m);

        var charge = await db.Charges.SingleAsync(c => c.Id == response.Id);
        await Assert.ThrowsAsync<ConflictException>(
            () => service.UpdateAsync(charge, NewRequest() with { Amount = 100m }, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_RemovesUnlockedCharge_AsASoftDelete()
    {
        var (db, service) = CreateService(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var response = await service.CreateAsync(property.Id, NewRequest(), CancellationToken.None);
        var charge = await db.Charges.SingleAsync(c => c.Id == response.Id);

        await service.DeleteAsync(charge, CancellationToken.None);

        Assert.Null(await db.Charges.SingleOrDefaultAsync(c => c.Id == response.Id));
        var raw = await db.Charges.IgnoreQueryFilters().SingleAsync(c => c.Id == response.Id);
        Assert.True(raw.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_ThrowsConflict_WhenChargeIsLocked()
    {
        var (db, service) = CreateService(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var response = await service.CreateAsync(property.Id, NewRequest(), CancellationToken.None);
        await AllocatePaymentAsync(db, property.Id, response.Id, 75m);

        var charge = await db.Charges.SingleAsync(c => c.Id == response.Id);
        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(charge, CancellationToken.None));
    }

    [Fact]
    public async Task VoidAsync_ThrowsConflict_WhenAlreadyVoided()
    {
        var (db, service) = CreateService(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var response = await service.CreateAsync(property.Id, NewRequest(), CancellationToken.None);
        var charge = await db.Charges.SingleAsync(c => c.Id == response.Id);
        await service.VoidAsync(charge, CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => service.VoidAsync(charge, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAdjustmentAsync_ThrowsValidation_WhenReasonIsBlank()
    {
        var (db, service) = CreateService(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var response = await service.CreateAsync(property.Id, NewRequest(), CancellationToken.None);
        var charge = await db.Charges.SingleAsync(c => c.Id == response.Id);

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAdjustmentAsync(
            charge, new CreateChargeAdjustmentRequest(AdjustmentType.CreditAdjustment, 25m, ""), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAdjustmentAsync_LowersOutstandingAmount_OnLockedCharge()
    {
        var (db, service) = CreateService(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var response = await service.CreateAsync(property.Id, NewRequest(), CancellationToken.None);
        await AllocatePaymentAsync(db, property.Id, response.Id, 75m);

        var charge = await db.Charges.SingleAsync(c => c.Id == response.Id);
        await service.CreateAdjustmentAsync(
            charge, new CreateChargeAdjustmentRequest(AdjustmentType.CreditAdjustment, 75m, "Goodwill credit"), CancellationToken.None);

        var updated = await service.BuildResponseAsync(charge, CancellationToken.None);
        Assert.Equal(0m, updated.OutstandingAmount);
        Assert.Equal(ChargePaymentStatus.Paid, updated.PaymentStatus);
    }
}
