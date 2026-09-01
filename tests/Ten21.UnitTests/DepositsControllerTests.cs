using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Controllers;
using Ten21.Application.Abstractions;
using Ten21.Application.Ledger;
using Ten21.Business.Charges;
using Ten21.Business.Deposits;
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

/// <summary>US-39: collecting and settling a security deposit -- held in escrow separate from
/// operating rental income until move-out. Same in-memory SQLite pattern as
/// CreditsControllerTests -- shares one DbContext across Charges/Deposits controllers.</summary>
public class DepositsControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HtmlInputSanitizer _sanitizer = new();
    private readonly IPdfService _pdfService = new QuestPdfService();

    static DepositsControllerTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    public DepositsControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, ChargesController Charges, DepositsController Deposits) CreateControllers(Guid tenantId)
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
        var chargeService = new ChargeService(db, new ChargeRepository(db), _sanitizer);
        var statementService = new StatementService(new StatementRepository(db), chargeService, _pdfService);
        var charges = new ChargesController(authorizationService, chargeService, statementService)
        {
            ControllerContext = TestControllerContext.Create(),
        };
        var depositService = new DepositService(db, new DepositRepository(db), _sanitizer);
        var deposits = new DepositsController(authorizationService, depositService)
        {
            ControllerContext = TestControllerContext.Create(),
        };
        return (db, charges, deposits);
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

    private static async Task SeedActiveLeaseAsync(Ten21DbContext db, Guid propertyId, Guid residentId)
    {
        db.Leases.Add(new Lease
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            ResidentId = residentId,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = LeaseStatus.FixedTerm,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> CreateChargeAsync(
        ChargesController charges, Guid propertyId, decimal amount, DateOnly dueDate, ChargeCategory category = ChargeCategory.BaseRent)
    {
        var request = new UpsertChargeRequest(
            Description: $"{category} charge", Amount: amount, DueDate: dueDate, AccountingCode: null, Category: category);
        var result = await charges.CreateCharge(propertyId, request, CancellationToken.None);
        return Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(result).Value).Id;
    }

    [Fact]
    public async Task CollectDeposit_WithExplicitResident_Persists()
    {
        var (db, charges, deposits) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);

        var result = await deposits.CollectDeposit(
            property.Id, new CollectDepositRequest(1200m, new DateOnly(2026, 1, 1), resident.Id), CancellationToken.None);

        var response = Assert.IsType<SecurityDepositResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Equal(1200m, response.OriginalAmount);
        Assert.Equal(1200m, response.AmountHeld);
        Assert.Equal(SecurityDepositStatus.Held, response.Status);
        Assert.Equal(resident.Id, response.ResidentProfileId);
    }

    [Fact]
    public async Task CollectDeposit_AutoDefaultsToActiveLeasePrimaryResident_WhenNotSpecified()
    {
        var (db, charges, deposits) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        await SeedActiveLeaseAsync(db, property.Id, resident.Id);

        var result = await deposits.CollectDeposit(
            property.Id, new CollectDepositRequest(1200m, new DateOnly(2026, 1, 1), null), CancellationToken.None);

        var response = Assert.IsType<SecurityDepositResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Equal(resident.Id, response.ResidentProfileId);
    }

    [Fact]
    public async Task CollectDeposit_ThrowsValidationException_WhenNoActiveLeaseAndNoResidentSpecified()
    {
        var (db, charges, deposits) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);

        await Assert.ThrowsAsync<ValidationException>(() => deposits.CollectDeposit(
            property.Id, new CollectDepositRequest(1200m, new DateOnly(2026, 1, 1), null), CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task CollectDeposit_ThrowsValidationException_WhenAmountIsNotPositive(decimal amount)
    {
        var (db, charges, deposits) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);

        await Assert.ThrowsAsync<ValidationException>(() => deposits.CollectDeposit(
            property.Id, new CollectDepositRequest(amount, new DateOnly(2026, 1, 1), resident.Id), CancellationToken.None));
    }

    [Fact]
    public async Task SettleDeposit_AppliesAgainstOutstandingCharges_AndRefundsTheRemainder()
    {
        var (db, charges, deposits) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var collected = await deposits.CollectDeposit(
            property.Id, new CollectDepositRequest(1000m, new DateOnly(2026, 1, 1), resident.Id), CancellationToken.None);
        var depositId = Assert.IsType<SecurityDepositResponse>(Assert.IsType<CreatedAtActionResult>(collected).Value).Id;
        var damageChargeId = await CreateChargeAsync(charges, property.Id, 300m, new DateOnly(2026, 9, 1), ChargeCategory.AddOn);

        var result = await deposits.SettleDeposit(
            property.Id, depositId, new SettleDepositRequest(RefundTenderType.Check, "CHK-DEP-1"), CancellationToken.None);

        var response = Assert.IsType<SettleDepositResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(300m, response.AmountAppliedToCharges);
        Assert.Equal(700m, response.AmountRefunded);
        Assert.Equal(SecurityDepositStatus.Settled, response.Deposit.Status);
        Assert.Equal(0m, response.Deposit.AmountHeld);
        Assert.NotNull(response.Refund);
        Assert.Equal(700m, response.Refund!.Amount);
        Assert.Equal(RefundReason.DepositReturn, response.Refund!.Reason);
        var allocation = Assert.Single(response.ChargeAllocations);
        Assert.Equal(damageChargeId, allocation.TargetChargeId);
        Assert.Equal(300m, allocation.AppliedAmount);

        var chargeAfter = Assert.IsType<ChargeResponse>(Assert.IsType<OkObjectResult>(
            await charges.GetCharge(property.Id, damageChargeId, CancellationToken.None)).Value);
        Assert.Equal(ChargePaymentStatus.Paid, chargeAfter.PaymentStatus);

        var statement = Assert.IsType<UnitStatementResponse>(Assert.IsType<OkObjectResult>(
            await charges.GetStatement(property.Id, CancellationToken.None)).Value);
        Assert.Equal(0m, statement.Balance);
        // Settled-via-deposit money must never look like rent actually received.
        Assert.Empty(statement.Payments);
    }

    [Fact]
    public async Task SettleDeposit_AppliesFullDeposit_WhenDuesExceedIt_NoRefund_AndAccountStatusShowsTerminatedWithBalance()
    {
        var (db, charges, deposits) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var collected = await deposits.CollectDeposit(
            property.Id, new CollectDepositRequest(500m, new DateOnly(2026, 1, 1), resident.Id), CancellationToken.None);
        var depositId = Assert.IsType<SecurityDepositResponse>(Assert.IsType<CreatedAtActionResult>(collected).Value).Id;
        await CreateChargeAsync(charges, property.Id, 1200m, new DateOnly(2026, 9, 1), ChargeCategory.AddOn);

        var result = await deposits.SettleDeposit(
            property.Id, depositId, new SettleDepositRequest(RefundTenderType.Check, null), CancellationToken.None);

        var response = Assert.IsType<SettleDepositResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(500m, response.AmountAppliedToCharges);
        Assert.Equal(0m, response.AmountRefunded);
        Assert.Null(response.Refund);

        var statement = Assert.IsType<UnitStatementResponse>(Assert.IsType<OkObjectResult>(
            await charges.GetStatement(property.Id, CancellationToken.None)).Value);
        Assert.Equal(700m, statement.Balance); // 1200 due - 500 applied
        Assert.Equal(AccountStatus.TerminatedWithBalance, statement.AccountStatus);
    }

    [Fact]
    public async Task SettleDeposit_ThrowsConflict_WhenAlreadySettled()
    {
        var (db, charges, deposits) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var collected = await deposits.CollectDeposit(
            property.Id, new CollectDepositRequest(500m, new DateOnly(2026, 1, 1), resident.Id), CancellationToken.None);
        var depositId = Assert.IsType<SecurityDepositResponse>(Assert.IsType<CreatedAtActionResult>(collected).Value).Id;
        await deposits.SettleDeposit(property.Id, depositId, new SettleDepositRequest(RefundTenderType.Check, null), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => deposits.SettleDeposit(
            property.Id, depositId, new SettleDepositRequest(RefundTenderType.Check, null), CancellationToken.None));
    }

    [Fact]
    public async Task SettleDeposit_ThrowsNotFound_WhenDepositBelongsToADifferentProperty()
    {
        var (db, charges, deposits) = CreateControllers(Guid.NewGuid());
        var propertyA = await SeedPropertyAsync(db);
        var propertyB = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, propertyA.Id);
        var collected = await deposits.CollectDeposit(
            propertyA.Id, new CollectDepositRequest(500m, new DateOnly(2026, 1, 1), resident.Id), CancellationToken.None);
        var depositId = Assert.IsType<SecurityDepositResponse>(Assert.IsType<CreatedAtActionResult>(collected).Value).Id;

        await Assert.ThrowsAsync<NotFoundException>(() => deposits.SettleDeposit(
            propertyB.Id, depositId, new SettleDepositRequest(RefundTenderType.Check, null), CancellationToken.None));
    }
}
