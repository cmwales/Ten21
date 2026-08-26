using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Charges;
using Ten21.Api.Contracts.Credits;
using Ten21.Api.Controllers;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Ten21.Infrastructure.Security;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-37: "Refund Credit Balance" -- disburses a resident's retained overpayment
/// credit back to them. Same in-memory SQLite pattern as CreditsControllerTests.</summary>
public class RefundsControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HtmlInputSanitizer _sanitizer = new();

    public RefundsControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, ChargesController Charges, PaymentsController Payments, RefundsController Refunds) CreateControllers(Guid tenantId)
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

        return (db, new ChargesController(db, _sanitizer), new PaymentsController(db, _sanitizer), new RefundsController(db, _sanitizer));
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

    private static async Task<ResidentProfile> SeedResidentAsync(Ten21DbContext db, Guid propertyId, string firstName = "Jamie")
    {
        var resident = new ResidentProfile
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            OccupantType = OccupantType.Primary,
            FirstName = firstName,
            LastName = "Rivera",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ResidentProfiles.Add(resident);
        await db.SaveChangesAsync();
        return resident;
    }

    private static async Task LogOverpaymentAsync(
        PaymentsController payments, Guid propertyId, Guid residentId, decimal amount, DateOnly paymentDate)
    {
        var request = new LogPaymentRequest(residentId, paymentDate, amount, TenderType.Check, null, null);
        await payments.LogPayment(propertyId, request, CancellationToken.None);
    }

    private static RefundCreditBalanceRequest NewRefundRequest(Guid residentId, decimal amount) => new(
        residentId, amount, new DateOnly(2026, 9, 10), RefundTenderType.Check, "CHK-2001");

    [Fact]
    public async Task RefundCreditBalance_CreatesRefund_AndDecrementsAvailableCredit()
    {
        var (db, charges, payments, refunds) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        await LogOverpaymentAsync(payments, property.Id, resident.Id, 200m, new DateOnly(2026, 8, 1));

        var result = await refunds.RefundCreditBalance(property.Id, NewRefundRequest(resident.Id, 75m), CancellationToken.None);

        var response = Assert.IsType<RefundTransactionResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Equal(75m, response.Amount);
        Assert.Equal(RefundReason.OverpaymentRefund, response.Reason);
        Assert.Equal("Jamie Rivera", response.ResidentName);

        var statement = Assert.IsType<UnitStatementResponse>(Assert.IsType<OkObjectResult>(
            await charges.GetStatement(property.Id, CancellationToken.None)).Value);
        Assert.Equal(125m, statement.AvailableCredit); // 200 - 75
        Assert.Equal(-125m, statement.Balance); // still owed back to the resident
        Assert.Equal(75m, Assert.Single(statement.Refunds).Amount);
    }

    [Fact]
    public async Task RefundCreditBalance_ThrowsConflict_WhenAmountExceedsAvailableCredit()
    {
        var (db, charges, payments, refunds) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        await LogOverpaymentAsync(payments, property.Id, resident.Id, 50m, new DateOnly(2026, 8, 1));

        await Assert.ThrowsAsync<ConflictException>(() => refunds.RefundCreditBalance(
            property.Id, NewRefundRequest(resident.Id, 100m), CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task RefundCreditBalance_ThrowsValidationException_WhenAmountIsNotPositive(decimal amount)
    {
        var (db, charges, payments, refunds) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);

        await Assert.ThrowsAsync<ValidationException>(() => refunds.RefundCreditBalance(
            property.Id, NewRefundRequest(resident.Id, amount), CancellationToken.None));
    }

    [Fact]
    public async Task RefundCreditBalance_DrawsDownOldestPaymentFirst()
    {
        var (db, charges, payments, refunds) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        await LogOverpaymentAsync(payments, property.Id, resident.Id, 30m, new DateOnly(2026, 7, 1));
        await LogOverpaymentAsync(payments, property.Id, resident.Id, 30m, new DateOnly(2026, 8, 1));

        await refunds.RefundCreditBalance(property.Id, NewRefundRequest(resident.Id, 40m), CancellationToken.None);

        var remainingPayments = await db.PaymentTransactions
            .Where(p => p.ResidentProfileId == resident.Id)
            .OrderBy(p => p.PaymentDate)
            .ToListAsync();
        Assert.Equal(0m, remainingPayments[0].UnallocatedAmount); // older payment fully drawn down
        Assert.Equal(20m, remainingPayments[1].UnallocatedAmount); // newer payment partially drawn down
    }

    [Fact]
    public async Task RefundCreditBalance_ThrowsNotFound_WhenResidentDoesNotBelongToThisProperty()
    {
        var (db, charges, payments, refunds) = CreateControllers(Guid.NewGuid());
        var propertyA = await SeedPropertyAsync(db);
        var propertyB = await SeedPropertyAsync(db);
        var residentOfB = await SeedResidentAsync(db, propertyB.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => refunds.RefundCreditBalance(
            propertyA.Id, NewRefundRequest(residentOfB.Id, 10m), CancellationToken.None));
    }

    [Fact]
    public async Task GetRefund_ThrowsNotFound_WhenRefundBelongsToADifferentProperty()
    {
        var (db, charges, payments, refunds) = CreateControllers(Guid.NewGuid());
        var propertyA = await SeedPropertyAsync(db);
        var propertyB = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, propertyA.Id);
        await LogOverpaymentAsync(payments, propertyA.Id, resident.Id, 50m, new DateOnly(2026, 8, 1));
        var created = await refunds.RefundCreditBalance(propertyA.Id, NewRefundRequest(resident.Id, 20m), CancellationToken.None);
        var id = Assert.IsType<RefundTransactionResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        await Assert.ThrowsAsync<NotFoundException>(() => refunds.GetRefund(propertyB.Id, id, CancellationToken.None));
    }
}
