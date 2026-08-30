using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Credits;
using Ten21.Api.Controllers;
using Ten21.Application.Abstractions;
using Ten21.Business.Charges;
using Ten21.Business.Payments;
using Ten21.Business.Statements;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Pdf;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Ten21.Infrastructure.Security;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-37: "Apply Credits to Charges" -- draws down a unit's retained overpayment
/// credit against its outstanding charges. A manual, PM-triggered action (not a scheduled
/// job -- see CreditsController's own comment). Same in-memory SQLite pattern as
/// PaymentsControllerTests -- shares one DbContext across Charges/Payments/Credits
/// controllers.</summary>
public class CreditsControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HtmlInputSanitizer _sanitizer = new();
    private readonly IPdfService _pdfService = new QuestPdfService();

    static CreditsControllerTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    public CreditsControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, ChargesController Charges, PaymentsController Payments, CreditsController Credits) CreateControllers(Guid tenantId)
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
        var chargeService = new ChargeService(new ChargeRepository(db), _sanitizer);
        var statementService = new StatementService(new StatementRepository(db), chargeService, _pdfService);
        var charges = new ChargesController(authorizationService, chargeService, statementService)
        {
            ControllerContext = TestControllerContext.Create(),
        };
        var paymentService = new PaymentService(new PaymentRepository(db), _sanitizer);
        var payments = new PaymentsController(_pdfService, authorizationService, paymentService)
        {
            ControllerContext = TestControllerContext.Create(),
        };
        return (db, charges, payments, new CreditsController(db));
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
            FirstName = "Jamie",
            LastName = "Rivera",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ResidentProfiles.Add(resident);
        await db.SaveChangesAsync();
        return resident;
    }

    private static async Task<Guid> CreateChargeAsync(
        ChargesController charges, Guid propertyId, decimal amount, DateOnly dueDate, ChargeCategory category = ChargeCategory.BaseRent)
    {
        var request = new UpsertChargeRequest(
            Description: $"{category} charge", Amount: amount, DueDate: dueDate, AccountingCode: null, Category: category);
        var result = await charges.CreateCharge(propertyId, request, CancellationToken.None);
        return Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(result).Value).Id;
    }

    private static async Task<Guid> LogOverpaymentAsync(
        PaymentsController payments, Guid propertyId, Guid residentId, decimal amount, DateOnly paymentDate)
    {
        var request = new LogPaymentRequest(residentId, paymentDate, amount, TenderType.Check, null, null);
        var result = await payments.LogPayment(propertyId, request, CancellationToken.None);
        return Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(result).Value).Id;
    }

    [Fact]
    public async Task ApplyCreditsToCharges_ReturnsZero_WhenNoCreditAvailable()
    {
        var (db, charges, payments, credits) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);

        var result = await credits.ApplyCreditsToCharges(property.Id, CancellationToken.None);

        var response = Assert.IsType<ApplyCreditsResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(0m, response.TotalApplied);
        Assert.Empty(response.Allocations);
    }

    [Fact]
    public async Task ApplyCreditsToCharges_DrawsDownRetainedCredit_AgainstNewlyPostedCharge()
    {
        var (db, charges, payments, credits) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        // Overpay $200 against nothing -- the whole thing becomes retained credit.
        var paymentId = await LogOverpaymentAsync(payments, property.Id, resident.Id, 200m, new DateOnly(2026, 8, 1));
        // A new charge shows up the next month -- exactly the "pre-payment satisfies upcoming rent" scenario.
        var chargeId = await CreateChargeAsync(charges, property.Id, 150m, new DateOnly(2026, 9, 1));

        var result = await credits.ApplyCreditsToCharges(property.Id, CancellationToken.None);

        var response = Assert.IsType<ApplyCreditsResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(150m, response.TotalApplied);
        var allocation = Assert.Single(response.Allocations);
        Assert.Equal(paymentId, allocation.SourcePaymentTransactionId);
        Assert.Equal(chargeId, allocation.TargetChargeId);
        Assert.Equal(150m, allocation.AppliedAmount);

        var chargeAfter = Assert.IsType<ChargeResponse>(Assert.IsType<OkObjectResult>(
            await charges.GetCharge(property.Id, chargeId, CancellationToken.None)).Value);
        Assert.Equal(ChargePaymentStatus.Paid, chargeAfter.PaymentStatus);
        Assert.True(chargeAfter.IsLocked);

        var statement = Assert.IsType<UnitStatementResponse>(Assert.IsType<OkObjectResult>(
            await charges.GetStatement(property.Id, CancellationToken.None)).Value);
        Assert.Equal(50m, statement.AvailableCredit); // 200 retained - 150 applied
    }

    [Fact]
    public async Task ApplyCreditsToCharges_AppliesInStatutoryPriorityOrder()
    {
        var (db, charges, payments, credits) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        await LogOverpaymentAsync(payments, property.Id, resident.Id, 100m, new DateOnly(2026, 8, 1));
        var rentId = await CreateChargeAsync(charges, property.Id, 1000m, new DateOnly(2026, 9, 1), ChargeCategory.BaseRent);
        var lateFeeId = await CreateChargeAsync(charges, property.Id, 50m, new DateOnly(2026, 9, 1), ChargeCategory.LateFee);

        var result = await credits.ApplyCreditsToCharges(property.Id, CancellationToken.None);

        var response = Assert.IsType<ApplyCreditsResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(2, response.Allocations.Count);
        Assert.Equal(50m, response.Allocations.Single(a => a.TargetChargeId == lateFeeId).AppliedAmount);
        Assert.Equal(50m, response.Allocations.Single(a => a.TargetChargeId == rentId).AppliedAmount);
    }

    [Fact]
    public async Task ApplyCreditsToCharges_CombinesMultiplePaymentSources_OldestFirst()
    {
        var (db, charges, payments, credits) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var olderPaymentId = await LogOverpaymentAsync(payments, property.Id, resident.Id, 40m, new DateOnly(2026, 7, 1));
        var newerPaymentId = await LogOverpaymentAsync(payments, property.Id, resident.Id, 40m, new DateOnly(2026, 8, 1));
        var chargeId = await CreateChargeAsync(charges, property.Id, 60m, new DateOnly(2026, 9, 1));

        var result = await credits.ApplyCreditsToCharges(property.Id, CancellationToken.None);

        var response = Assert.IsType<ApplyCreditsResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(2, response.Allocations.Count); // 40 from the older payment + 20 from the newer
        Assert.Equal(40m, response.Allocations.Single(a => a.SourcePaymentTransactionId == olderPaymentId).AppliedAmount);
        Assert.Equal(20m, response.Allocations.Single(a => a.SourcePaymentTransactionId == newerPaymentId).AppliedAmount);
        Assert.Equal(chargeId, response.Allocations[0].TargetChargeId);
    }

    [Fact]
    public async Task ApplyCreditsToCharges_SkipsVoidedCharges()
    {
        var (db, charges, payments, credits) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        await LogOverpaymentAsync(payments, property.Id, resident.Id, 100m, new DateOnly(2026, 8, 1));
        var voidedId = await CreateChargeAsync(charges, property.Id, 50m, new DateOnly(2026, 9, 1), ChargeCategory.LateFee);
        await charges.VoidCharge(property.Id, voidedId, CancellationToken.None);
        var rentId = await CreateChargeAsync(charges, property.Id, 60m, new DateOnly(2026, 9, 1), ChargeCategory.BaseRent);

        var result = await credits.ApplyCreditsToCharges(property.Id, CancellationToken.None);

        var response = Assert.IsType<ApplyCreditsResponse>(Assert.IsType<OkObjectResult>(result).Value);
        var allocation = Assert.Single(response.Allocations);
        Assert.Equal(rentId, allocation.TargetChargeId);
    }

    [Fact]
    public async Task ApplyCreditsToCharges_ThrowsNotFound_WhenPropertyDoesNotExist()
    {
        var (db, charges, payments, credits) = CreateControllers(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() => credits.ApplyCreditsToCharges(Guid.NewGuid(), CancellationToken.None));
    }
}
