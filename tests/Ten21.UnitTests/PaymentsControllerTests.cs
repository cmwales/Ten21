using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Charges;
using Ten21.Api.Controllers;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Ten21.Infrastructure.Security;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-34: logging a manually-received payment and the statutory waterfall allocation
/// it triggers against a unit's outstanding Charges. Same in-memory SQLite pattern as
/// ChargesControllerTests -- shares the same DbContext between a ChargesController (to seed
/// charges) and a PaymentsController (the thing under test).</summary>
public class PaymentsControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HtmlInputSanitizer _sanitizer = new();

    public PaymentsControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, ChargesController Charges, PaymentsController Payments) CreateControllers(Guid tenantId)
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

        return (db, new ChargesController(db, _sanitizer), new PaymentsController(db, _sanitizer));
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

    private static async Task<Guid> CreateChargeAsync(
        ChargesController charges, Guid propertyId, ChargeCategory category, decimal amount, DateOnly dueDate)
    {
        var request = new UpsertChargeRequest(
            Description: $"{category} charge",
            Amount: amount,
            DueDate: dueDate,
            AccountingCode: null,
            Category: category);

        var result = await charges.CreateCharge(propertyId, request, CancellationToken.None);
        return Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(result).Value).Id;
    }

    private static LogPaymentRequest NewPaymentRequest(decimal amount) => new(
        PaymentDate: new DateOnly(2026, 9, 1),
        AmountPaid: amount,
        TenderType: TenderType.Check,
        ReferenceNumber: "CHK-1001",
        Notes: "Paid at leasing office");

    [Fact]
    public async Task LogPayment_AllocatesFullAmount_ToSingleOutstandingCharge()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var chargeId = await CreateChargeAsync(charges, property.Id, ChargeCategory.BaseRent, 1450m, new DateOnly(2026, 9, 1));

        var result = await payments.LogPayment(property.Id, NewPaymentRequest(1450m), CancellationToken.None);

        var response = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        var allocation = Assert.Single(response.Allocations);
        Assert.Equal(chargeId, allocation.ChargeId);
        Assert.Equal(1450m, allocation.AllocatedAmount);

        var chargeAfter = Assert.IsType<ChargeResponse>(Assert.IsType<OkObjectResult>(
            await charges.GetCharge(property.Id, chargeId, CancellationToken.None)).Value);
        Assert.Equal(ChargePaymentStatus.Paid, chargeAfter.PaymentStatus);
    }

    [Fact]
    public async Task LogPayment_AppliesStatutoryPriorityOrder_LateFeeAndLegalBeforeBaseRent()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var rentId = await CreateChargeAsync(charges, property.Id, ChargeCategory.BaseRent, 1000m, new DateOnly(2026, 9, 1));
        var lateFeeId = await CreateChargeAsync(charges, property.Id, ChargeCategory.LateFee, 50m, new DateOnly(2026, 9, 1));
        var legalId = await CreateChargeAsync(charges, property.Id, ChargeCategory.Legal, 100m, new DateOnly(2026, 9, 1));

        // Only enough to cover the LateFee (priority 1) and Legal (priority 2) charges, plus a
        // little left toward BaseRent (priority 3) -- proves priority order, not creation order
        // or amount order, drives allocation.
        var result = await payments.LogPayment(property.Id, NewPaymentRequest(170m), CancellationToken.None);

        var response = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Equal(3, response.Allocations.Count);
        Assert.Equal(50m, response.Allocations.Single(a => a.ChargeId == lateFeeId).AllocatedAmount);
        Assert.Equal(100m, response.Allocations.Single(a => a.ChargeId == legalId).AllocatedAmount);
        Assert.Equal(20m, response.Allocations.Single(a => a.ChargeId == rentId).AllocatedAmount);
    }

    [Fact]
    public async Task LogPayment_TieBreaksBySameDueDate_WhenPriorityMatches()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var olderAddOnId = await CreateChargeAsync(charges, property.Id, ChargeCategory.AddOn, 60m, new DateOnly(2026, 8, 1));
        var newerAddOnId = await CreateChargeAsync(charges, property.Id, ChargeCategory.AddOn, 60m, new DateOnly(2026, 9, 1));

        var result = await payments.LogPayment(property.Id, NewPaymentRequest(60m), CancellationToken.None);

        var response = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        var allocation = Assert.Single(response.Allocations);
        Assert.Equal(olderAddOnId, allocation.ChargeId);

        var newerCharge = Assert.IsType<ChargeResponse>(Assert.IsType<OkObjectResult>(
            await charges.GetCharge(property.Id, newerAddOnId, CancellationToken.None)).Value);
        Assert.Equal(ChargePaymentStatus.Unpaid, newerCharge.PaymentStatus);
    }

    [Fact]
    public async Task LogPayment_PartiallyAllocates_WhenAmountIsLessThanOutstanding()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var chargeId = await CreateChargeAsync(charges, property.Id, ChargeCategory.BaseRent, 1450m, new DateOnly(2026, 9, 1));

        var result = await payments.LogPayment(property.Id, NewPaymentRequest(500m), CancellationToken.None);

        var response = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Equal(500m, Assert.Single(response.Allocations).AllocatedAmount);

        var chargeAfter = Assert.IsType<ChargeResponse>(Assert.IsType<OkObjectResult>(
            await charges.GetCharge(property.Id, chargeId, CancellationToken.None)).Value);
        Assert.Equal(ChargePaymentStatus.Partial, chargeAfter.PaymentStatus);
        Assert.Equal(950m, chargeAfter.OutstandingAmount);
    }

    [Fact]
    public async Task LogPayment_LeavesRemainderUnallocated_OnOverpayment()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        await CreateChargeAsync(charges, property.Id, ChargeCategory.BaseRent, 500m, new DateOnly(2026, 9, 1));

        var result = await payments.LogPayment(property.Id, NewPaymentRequest(700m), CancellationToken.None);

        var response = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        // Only the 500 that had somewhere to go is allocated; the extra 200 is recorded on the
        // payment (AmountPaid) but not tied to any charge -- it surfaces as a credit via the
        // unit statement's Balance formula instead.
        Assert.Equal(500m, Assert.Single(response.Allocations).AllocatedAmount);
        Assert.Equal(700m, response.AmountPaid);

        var statement = Assert.IsType<UnitStatementResponse>(Assert.IsType<OkObjectResult>(
            await charges.GetStatement(property.Id, CancellationToken.None)).Value);
        Assert.Equal(-200m, statement.Balance);
    }

    [Fact]
    public async Task LogPayment_SkipsVoidedCharges()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var voidedId = await CreateChargeAsync(charges, property.Id, ChargeCategory.LateFee, 50m, new DateOnly(2026, 9, 1));
        var rentId = await CreateChargeAsync(charges, property.Id, ChargeCategory.BaseRent, 1000m, new DateOnly(2026, 9, 1));
        var voidedCharge = await db.Charges.SingleAsync(c => c.Id == voidedId);
        voidedCharge.Status = ChargeLifecycleStatus.Voided;
        await db.SaveChangesAsync();

        var result = await payments.LogPayment(property.Id, NewPaymentRequest(50m), CancellationToken.None);

        var response = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        var allocation = Assert.Single(response.Allocations);
        Assert.Equal(rentId, allocation.ChargeId);
    }

    [Fact]
    public async Task LogPayment_SkipsAlreadyFullyPaidCharges_AndAppliesToNextOutstanding()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var lateFeeId = await CreateChargeAsync(charges, property.Id, ChargeCategory.LateFee, 50m, new DateOnly(2026, 9, 1));
        var rentId = await CreateChargeAsync(charges, property.Id, ChargeCategory.BaseRent, 1000m, new DateOnly(2026, 9, 1));
        await payments.LogPayment(property.Id, NewPaymentRequest(50m), CancellationToken.None);

        var result = await payments.LogPayment(property.Id, NewPaymentRequest(1000m), CancellationToken.None);

        var response = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        var allocation = Assert.Single(response.Allocations);
        Assert.Equal(rentId, allocation.ChargeId);
        Assert.Equal(1000m, allocation.AllocatedAmount);
    }

    [Fact]
    public async Task LogPayment_ThrowsValidationException_WhenAmountIsNotPositive()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() => payments.LogPayment(
            property.Id, NewPaymentRequest(0m), CancellationToken.None));
    }

    [Fact]
    public async Task LogPayment_ThrowsNotFound_WhenPropertyDoesNotExist()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() => payments.LogPayment(
            Guid.NewGuid(), NewPaymentRequest(100m), CancellationToken.None));
    }

    [Fact]
    public async Task GetPayment_ThrowsNotFound_WhenPaymentBelongsToADifferentProperty()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var propertyA = await SeedPropertyAsync(db);
        var propertyB = await SeedPropertyAsync(db);
        await CreateChargeAsync(charges, propertyA.Id, ChargeCategory.BaseRent, 500m, new DateOnly(2026, 9, 1));
        var created = await payments.LogPayment(propertyA.Id, NewPaymentRequest(500m), CancellationToken.None);
        var id = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        await Assert.ThrowsAsync<NotFoundException>(() => payments.GetPayment(propertyB.Id, id, CancellationToken.None));
    }
}
