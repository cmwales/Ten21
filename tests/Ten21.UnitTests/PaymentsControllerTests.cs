using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Charges;
using Ten21.Api.Controllers;
using Ten21.Application.Abstractions;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Pdf;
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
    private readonly IPdfService _pdfService = new QuestPdfService();

    static PaymentsControllerTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

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

        return (db, new ChargesController(db, _sanitizer, _pdfService), new PaymentsController(db, _sanitizer, _pdfService));
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

    private static LogPaymentRequest NewPaymentRequest(Guid residentProfileId, decimal amount) => new(
        ResidentProfileId: residentProfileId,
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
        var resident = await SeedResidentAsync(db, property.Id);
        var chargeId = await CreateChargeAsync(charges, property.Id, ChargeCategory.BaseRent, 1450m, new DateOnly(2026, 9, 1));

        var result = await payments.LogPayment(property.Id, NewPaymentRequest(resident.Id, 1450m), CancellationToken.None);

        var response = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Equal(resident.Id, response.ResidentProfileId);
        Assert.Equal("Jamie Rivera", response.ResidentName);
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
        var resident = await SeedResidentAsync(db, property.Id);
        var rentId = await CreateChargeAsync(charges, property.Id, ChargeCategory.BaseRent, 1000m, new DateOnly(2026, 9, 1));
        var lateFeeId = await CreateChargeAsync(charges, property.Id, ChargeCategory.LateFee, 50m, new DateOnly(2026, 9, 1));
        var legalId = await CreateChargeAsync(charges, property.Id, ChargeCategory.Legal, 100m, new DateOnly(2026, 9, 1));

        // Only enough to cover the LateFee (priority 1) and Legal (priority 2) charges, plus a
        // little left toward BaseRent (priority 3) -- proves priority order, not creation order
        // or amount order, drives allocation.
        var result = await payments.LogPayment(property.Id, NewPaymentRequest(resident.Id, 170m), CancellationToken.None);

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
        var resident = await SeedResidentAsync(db, property.Id);
        var olderAddOnId = await CreateChargeAsync(charges, property.Id, ChargeCategory.AddOn, 60m, new DateOnly(2026, 8, 1));
        var newerAddOnId = await CreateChargeAsync(charges, property.Id, ChargeCategory.AddOn, 60m, new DateOnly(2026, 9, 1));

        var result = await payments.LogPayment(property.Id, NewPaymentRequest(resident.Id, 60m), CancellationToken.None);

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
        var resident = await SeedResidentAsync(db, property.Id);
        var chargeId = await CreateChargeAsync(charges, property.Id, ChargeCategory.BaseRent, 1450m, new DateOnly(2026, 9, 1));

        var result = await payments.LogPayment(property.Id, NewPaymentRequest(resident.Id, 500m), CancellationToken.None);

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
        var resident = await SeedResidentAsync(db, property.Id);
        await CreateChargeAsync(charges, property.Id, ChargeCategory.BaseRent, 500m, new DateOnly(2026, 9, 1));

        var result = await payments.LogPayment(property.Id, NewPaymentRequest(resident.Id, 700m), CancellationToken.None);

        var response = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        // Only the 500 that had somewhere to go is allocated; the extra 200 is recorded on the
        // payment (AmountPaid, still attributed to this resident so it's refundable to them --
        // see PaymentTransaction's own comment) but not tied to any charge -- it surfaces as a
        // credit via the unit statement's Balance formula instead.
        Assert.Equal(500m, Assert.Single(response.Allocations).AllocatedAmount);
        Assert.Equal(700m, response.AmountPaid);
        Assert.Equal(resident.Id, response.ResidentProfileId);

        var statement = Assert.IsType<UnitStatementResponse>(Assert.IsType<OkObjectResult>(
            await charges.GetStatement(property.Id, CancellationToken.None)).Value);
        Assert.Equal(-200m, statement.Balance);
        Assert.Equal(resident.Id, Assert.Single(statement.Payments).ResidentProfileId);
    }

    [Fact]
    public async Task LogPayment_SkipsVoidedCharges()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var voidedId = await CreateChargeAsync(charges, property.Id, ChargeCategory.LateFee, 50m, new DateOnly(2026, 9, 1));
        var rentId = await CreateChargeAsync(charges, property.Id, ChargeCategory.BaseRent, 1000m, new DateOnly(2026, 9, 1));
        var voidedCharge = await db.Charges.SingleAsync(c => c.Id == voidedId);
        voidedCharge.Status = ChargeLifecycleStatus.Voided;
        await db.SaveChangesAsync();

        var result = await payments.LogPayment(property.Id, NewPaymentRequest(resident.Id, 50m), CancellationToken.None);

        var response = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        var allocation = Assert.Single(response.Allocations);
        Assert.Equal(rentId, allocation.ChargeId);
    }

    [Fact]
    public async Task LogPayment_SkipsAlreadyFullyPaidCharges_AndAppliesToNextOutstanding()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var lateFeeId = await CreateChargeAsync(charges, property.Id, ChargeCategory.LateFee, 50m, new DateOnly(2026, 9, 1));
        var rentId = await CreateChargeAsync(charges, property.Id, ChargeCategory.BaseRent, 1000m, new DateOnly(2026, 9, 1));
        await payments.LogPayment(property.Id, NewPaymentRequest(resident.Id, 50m), CancellationToken.None);

        var result = await payments.LogPayment(property.Id, NewPaymentRequest(resident.Id, 1000m), CancellationToken.None);

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
        var resident = await SeedResidentAsync(db, property.Id);

        await Assert.ThrowsAsync<ValidationException>(() => payments.LogPayment(
            property.Id, NewPaymentRequest(resident.Id, 0m), CancellationToken.None));
    }

    [Fact]
    public async Task LogPayment_ThrowsNotFound_WhenPropertyDoesNotExist()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() => payments.LogPayment(
            Guid.NewGuid(), NewPaymentRequest(Guid.NewGuid(), 100m), CancellationToken.None));
    }

    [Fact]
    public async Task LogPayment_ThrowsNotFound_WhenResidentDoesNotBelongToThisProperty()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var propertyA = await SeedPropertyAsync(db);
        var propertyB = await SeedPropertyAsync(db);
        var residentOfB = await SeedResidentAsync(db, propertyB.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => payments.LogPayment(
            propertyA.Id, NewPaymentRequest(residentOfB.Id, 100m), CancellationToken.None));
    }

    [Fact]
    public async Task GetPayment_ThrowsNotFound_WhenPaymentBelongsToADifferentProperty()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var propertyA = await SeedPropertyAsync(db);
        var propertyB = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, propertyA.Id);
        await CreateChargeAsync(charges, propertyA.Id, ChargeCategory.BaseRent, 500m, new DateOnly(2026, 9, 1));
        var created = await payments.LogPayment(propertyA.Id, NewPaymentRequest(resident.Id, 500m), CancellationToken.None);
        var id = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        await Assert.ThrowsAsync<NotFoundException>(() => payments.GetPayment(propertyB.Id, id, CancellationToken.None));
    }

    [Fact]
    public async Task ReversePayment_RestoresTargetChargeToUnpaid_AndZeroesUnallocatedAmount()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var chargeId = await CreateChargeAsync(charges, property.Id, ChargeCategory.BaseRent, 500m, new DateOnly(2026, 9, 1));
        // Overpay so there's retained credit too -- reversal should zero that out as well.
        var created = await payments.LogPayment(property.Id, NewPaymentRequest(resident.Id, 600m), CancellationToken.None);
        var id = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await payments.ReversePayment(property.Id, id, new ReversePaymentRequest("Bounced check, bank fee assessed"), CancellationToken.None);

        var response = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(PaymentTransactionStatus.Reversed, response.Status);
        Assert.Equal(0m, response.UnallocatedAmount);
        Assert.Empty(response.Allocations);

        var chargeAfter = Assert.IsType<ChargeResponse>(Assert.IsType<OkObjectResult>(
            await charges.GetCharge(property.Id, chargeId, CancellationToken.None)).Value);
        Assert.Equal(ChargePaymentStatus.Unpaid, chargeAfter.PaymentStatus);
        Assert.False(chargeAfter.IsLocked);

        var statement = Assert.IsType<UnitStatementResponse>(Assert.IsType<OkObjectResult>(
            await charges.GetStatement(property.Id, CancellationToken.None)).Value);
        Assert.Equal(500m, statement.Balance); // the reversed payment no longer counts as received
        Assert.Equal(0m, statement.AvailableCredit);
    }

    [Fact]
    public async Task ReversePayment_ThrowsConflict_WhenAlreadyReversed()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var created = await payments.LogPayment(property.Id, NewPaymentRequest(resident.Id, 100m), CancellationToken.None);
        var id = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;
        await payments.ReversePayment(property.Id, id, new ReversePaymentRequest("NSF"), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => payments.ReversePayment(
            property.Id, id, new ReversePaymentRequest("NSF again"), CancellationToken.None));
    }

    [Fact]
    public async Task ReversePayment_ThrowsValidationException_WhenReasonIsMissing()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var created = await payments.LogPayment(property.Id, NewPaymentRequest(resident.Id, 100m), CancellationToken.None);
        var id = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        await Assert.ThrowsAsync<ValidationException>(() => payments.ReversePayment(
            property.Id, id, new ReversePaymentRequest(""), CancellationToken.None));
    }

    [Fact]
    public async Task ReallocatePayment_ReversesOriginal_AndCreatesLinkedPaymentOnTheCorrectProperty()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var wrongProperty = await SeedPropertyAsync(db);
        var correctProperty = await SeedPropertyAsync(db);
        var residentOnWrongProperty = await SeedResidentAsync(db, wrongProperty.Id);
        var residentOnCorrectProperty = await SeedResidentAsync(db, correctProperty.Id);
        var correctChargeId = await CreateChargeAsync(charges, correctProperty.Id, ChargeCategory.BaseRent, 500m, new DateOnly(2026, 9, 1));
        var created = await payments.LogPayment(wrongProperty.Id, NewPaymentRequest(residentOnWrongProperty.Id, 500m), CancellationToken.None);
        var originalId = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await payments.ReallocatePayment(
            wrongProperty.Id, originalId,
            new ReallocatePaymentRequest(correctProperty.Id, residentOnCorrectProperty.Id, "Posted to the wrong door by mistake"),
            CancellationToken.None);

        var response = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Equal(correctProperty.Id, response.PropertyId);
        Assert.Equal(residentOnCorrectProperty.Id, response.ResidentProfileId);
        var allocation = Assert.Single(response.Allocations);
        Assert.Equal(correctChargeId, allocation.ChargeId);
        Assert.Equal(500m, allocation.AllocatedAmount);

        var original = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<OkObjectResult>(
            await payments.GetPayment(wrongProperty.Id, originalId, CancellationToken.None)).Value);
        Assert.Equal(PaymentTransactionStatus.Reversed, original.Status);
        Assert.Equal(response.Id, original.ReallocatedToId);
        Assert.Empty(original.Allocations);

        var wrongPropertyStatement = Assert.IsType<UnitStatementResponse>(Assert.IsType<OkObjectResult>(
            await charges.GetStatement(wrongProperty.Id, CancellationToken.None)).Value);
        Assert.Equal(0m, wrongPropertyStatement.Balance);

        var correctPropertyStatement = Assert.IsType<UnitStatementResponse>(Assert.IsType<OkObjectResult>(
            await charges.GetStatement(correctProperty.Id, CancellationToken.None)).Value);
        Assert.Equal(0m, correctPropertyStatement.Balance);
    }

    [Fact]
    public async Task ReallocatePayment_ThrowsValidationException_WhenTargetIsTheSameProperty()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        var created = await payments.LogPayment(property.Id, NewPaymentRequest(resident.Id, 100m), CancellationToken.None);
        var id = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        await Assert.ThrowsAsync<ValidationException>(() => payments.ReallocatePayment(
            property.Id, id, new ReallocatePaymentRequest(property.Id, resident.Id, "typo"), CancellationToken.None));
    }

    [Fact]
    public async Task ReallocatePayment_ThrowsNotFound_WhenTargetResidentDoesNotBelongToTargetProperty()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var wrongProperty = await SeedPropertyAsync(db);
        var correctProperty = await SeedPropertyAsync(db);
        var otherProperty = await SeedPropertyAsync(db);
        var residentOnWrongProperty = await SeedResidentAsync(db, wrongProperty.Id);
        var residentOnOtherProperty = await SeedResidentAsync(db, otherProperty.Id);
        var created = await payments.LogPayment(wrongProperty.Id, NewPaymentRequest(residentOnWrongProperty.Id, 100m), CancellationToken.None);
        var id = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        await Assert.ThrowsAsync<NotFoundException>(() => payments.ReallocatePayment(
            wrongProperty.Id, id, new ReallocatePaymentRequest(correctProperty.Id, residentOnOtherProperty.Id, "wrong resident"), CancellationToken.None));
    }

    [Fact]
    public async Task GetReceipt_ReturnsANonEmptyPdf()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        await CreateChargeAsync(charges, property.Id, ChargeCategory.BaseRent, 500m, new DateOnly(2026, 9, 1));
        var created = await payments.LogPayment(property.Id, NewPaymentRequest(resident.Id, 500m), CancellationToken.None);
        var id = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await payments.GetReceipt(property.Id, id, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.True(file.FileContents.Length > 0);
        // A real PDF file always starts with this magic header.
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(file.FileContents, 0, 4));
    }

    [Fact]
    public async Task GetReceipt_ThrowsNotFound_WhenPaymentBelongsToADifferentProperty()
    {
        var (db, charges, payments) = CreateControllers(Guid.NewGuid());
        var propertyA = await SeedPropertyAsync(db);
        var propertyB = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, propertyA.Id);
        var created = await payments.LogPayment(propertyA.Id, NewPaymentRequest(resident.Id, 100m), CancellationToken.None);
        var id = Assert.IsType<PaymentTransactionResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        await Assert.ThrowsAsync<NotFoundException>(() => payments.GetReceipt(propertyB.Id, id, CancellationToken.None));
    }
}
