using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Business.Charges;
using Ten21.Business.Payments;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Ten21.Infrastructure.Security;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>
/// Business-layer refactor: exercises PaymentService directly, with no ChargesController/
/// PaymentsController/HTTP layer involved -- same rationale as ChargeServiceTests. Full
/// end-to-end coverage (including resource authorization) still lives in
/// PaymentsControllerTests.cs; this file only re-covers the waterfall/reversal business
/// rules that moved.
/// </summary>
public class PaymentServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HtmlInputSanitizer _sanitizer = new();

    public PaymentServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, ChargeService Charges, PaymentService Payments) CreateServices(Guid tenantId)
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

        return (db, new ChargeService(db, new ChargeRepository(db), _sanitizer), new PaymentService(db, new PaymentRepository(db), _sanitizer));
    }

    private static async Task<Property> SeedPropertyAsync(Ten21DbContext db, string streetAddress1 = "100 Main St")
    {
        var property = new Property
        {
            Id = Guid.NewGuid(),
            Name = "Riverside Apartments",
            PropertyType = PropertyType.MultiFamily,
            StreetAddress1 = streetAddress1,
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
            FirstName = "Jamie",
            LastName = "Rivera",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ResidentProfiles.Add(resident);
        await db.SaveChangesAsync();
        return resident;
    }

    [Fact]
    public async Task LogPaymentAsync_AllocatesFullyAgainstOneOutstandingCharge()
    {
        var (db, charges, payments) = CreateServices(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        await charges.CreateAsync(
            property.Id,
            new UpsertChargeRequest("September Rent", 1000m, new DateOnly(2026, 9, 1), null, ChargeCategory.BaseRent),
            CancellationToken.None);

        var response = await payments.LogPaymentAsync(
            property.Id,
            new LogPaymentRequest(resident.Id, new DateOnly(2026, 9, 2), 1000m, TenderType.Check, null, null),
            CancellationToken.None);

        Assert.Single(response.Allocations);
        Assert.Equal(1000m, response.Allocations[0].AllocatedAmount);
        Assert.Equal(0m, response.UnallocatedAmount);
    }

    [Fact]
    public async Task LogPaymentAsync_LeavesOverpaymentUnallocated()
    {
        var (db, charges, payments) = CreateServices(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        await charges.CreateAsync(
            property.Id,
            new UpsertChargeRequest("September Rent", 1000m, new DateOnly(2026, 9, 1), null, ChargeCategory.BaseRent),
            CancellationToken.None);

        var response = await payments.LogPaymentAsync(
            property.Id,
            new LogPaymentRequest(resident.Id, new DateOnly(2026, 9, 2), 1200m, TenderType.Check, null, null),
            CancellationToken.None);

        Assert.Equal(1000m, response.Allocations.Single().AllocatedAmount);
        Assert.Equal(200m, response.UnallocatedAmount);
    }

    [Fact]
    public async Task LogPaymentAsync_ThrowsNotFound_WhenResidentDoesNotBelongToProperty()
    {
        var (db, _, payments) = CreateServices(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(() => payments.LogPaymentAsync(
            property.Id,
            new LogPaymentRequest(Guid.NewGuid(), new DateOnly(2026, 9, 2), 500m, TenderType.Check, null, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task ReverseAsync_UnlinksAllocations_AndRestoresChargeToUnpaid()
    {
        var (db, charges, payments) = CreateServices(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var chargeResponse = await charges.CreateAsync(
            property.Id,
            new UpsertChargeRequest("September Rent", 1000m, new DateOnly(2026, 9, 1), null, ChargeCategory.BaseRent),
            CancellationToken.None);
        var paymentResponse = await payments.LogPaymentAsync(
            property.Id,
            new LogPaymentRequest(resident.Id, new DateOnly(2026, 9, 2), 1000m, TenderType.Check, null, null),
            CancellationToken.None);

        var payment = await db.PaymentTransactions.Include(p => p.Allocations).SingleAsync(p => p.Id == paymentResponse.Id);
        var reversed = await payments.ReverseAsync(payment, new ReversePaymentRequest("NSF"), CancellationToken.None);

        Assert.Equal(PaymentTransactionStatus.Reversed, reversed.Status);
        Assert.Empty(reversed.Allocations);
        var charge = await charges.FindAsync(property.Id, chargeResponse.Id, CancellationToken.None);
        var chargeAfter = await charges.BuildResponseAsync(charge!, CancellationToken.None);
        Assert.Equal(ChargePaymentStatus.Unpaid, chargeAfter.PaymentStatus);
    }

    [Fact]
    public async Task ReverseAsync_ThrowsConflict_WhenAlreadyReversed()
    {
        var (db, _, payments) = CreateServices(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var paymentResponse = await payments.LogPaymentAsync(
            property.Id,
            new LogPaymentRequest(resident.Id, new DateOnly(2026, 9, 2), 500m, TenderType.Check, null, null),
            CancellationToken.None);

        var payment = await db.PaymentTransactions.Include(p => p.Allocations).SingleAsync(p => p.Id == paymentResponse.Id);
        await payments.ReverseAsync(payment, new ReversePaymentRequest("NSF"), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(
            () => payments.ReverseAsync(payment, new ReversePaymentRequest("NSF again"), CancellationToken.None));
    }

    [Fact]
    public async Task ReallocateAsync_ThrowsValidation_WhenTargetIsTheSameProperty()
    {
        var (db, _, payments) = CreateServices(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var paymentResponse = await payments.LogPaymentAsync(
            property.Id,
            new LogPaymentRequest(resident.Id, new DateOnly(2026, 9, 2), 500m, TenderType.Check, null, null),
            CancellationToken.None);

        var payment = await db.PaymentTransactions.Include(p => p.Allocations).SingleAsync(p => p.Id == paymentResponse.Id);
        await Assert.ThrowsAsync<ValidationException>(() => payments.ReallocateAsync(
            payment, property.Id, new ReallocatePaymentRequest(property.Id, resident.Id, "typo"), CancellationToken.None));
    }

    [Fact]
    public async Task ReallocateAsync_MovesPaymentToTargetProperty_AndReversesOriginal()
    {
        var (db, _, payments) = CreateServices(Guid.NewGuid());
        var wrongProperty = await SeedPropertyAsync(db, "999 Wrong St");
        var correctProperty = await SeedPropertyAsync(db, "1 Correct Ave");
        var residentOnWrongProperty = await SeedResidentAsync(db, wrongProperty.Id);
        var residentOnCorrectProperty = await SeedResidentAsync(db, correctProperty.Id);

        var paymentResponse = await payments.LogPaymentAsync(
            wrongProperty.Id,
            new LogPaymentRequest(residentOnWrongProperty.Id, new DateOnly(2026, 9, 2), 500m, TenderType.Check, null, null),
            CancellationToken.None);
        var payment = await db.PaymentTransactions.Include(p => p.Allocations).SingleAsync(p => p.Id == paymentResponse.Id);

        var reallocated = await payments.ReallocateAsync(
            payment, wrongProperty.Id,
            new ReallocatePaymentRequest(correctProperty.Id, residentOnCorrectProperty.Id, "Posted to the wrong door"),
            CancellationToken.None);

        Assert.Equal(correctProperty.Id, reallocated.PropertyId);
        var original = await db.PaymentTransactions.SingleAsync(p => p.Id == payment.Id);
        Assert.Equal(PaymentTransactionStatus.Reversed, original.Status);
        Assert.Equal(reallocated.Id, original.ReallocatedToId);
    }
}
