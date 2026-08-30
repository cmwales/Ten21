using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Controllers;
using Ten21.Application.Abstractions;
using Ten21.Business.Charges;
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

/// <summary>Sprint 7 (renamed from ManualChargesControllerTests): general billable line items
/// on a unit's ledger, nested under a Property. Same in-memory SQLite pattern as
/// LeasesControllerTests.</summary>
public class ChargesControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HtmlInputSanitizer _sanitizer = new();
    private readonly IPdfService _pdfService = new QuestPdfService();

    static ChargesControllerTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    public ChargesControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, ChargesController Controller) CreateController(Guid tenantId)
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
        var controller = new ChargesController(authorizationService, chargeService, statementService)
        {
            ControllerContext = TestControllerContext.Create(),
        };
        return (db, controller);
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

    private static async Task AllocatePaymentAsync(Ten21DbContext db, Guid chargeId, decimal amount, DateOnly? paymentDate = null)
    {
        var propertyId = (await db.Charges.SingleAsync(c => c.Id == chargeId)).PropertyId;
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
            PaymentDate = paymentDate ?? new DateOnly(2026, 9, 16),
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
    public async Task CreateCharge_Persists_UnpaidWithDerivedAllocationPriority()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);

        var result = await controller.CreateCharge(property.Id, NewRequest(ChargeCategory.BaseRent), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<ChargeResponse>(created.Value);
        Assert.Equal(property.Id, response.PropertyId);
        Assert.Equal(ChargeCategory.BaseRent, response.Category);
        Assert.Equal(ChargePaymentStatus.Unpaid, response.PaymentStatus);
        Assert.False(response.IsLocked);
        Assert.Equal(75m, response.OutstandingAmount);
        Assert.Equal(1, await db.Charges.CountAsync());

        var stored = await db.Charges.SingleAsync();
        Assert.Equal(Charge.DefaultAllocationPriorityFor(ChargeCategory.BaseRent), stored.AllocationPriority);
    }

    [Fact]
    public async Task CreateCharge_PersistsOptionalNotes()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var request = NewRequest() with { Notes = "Tenant disputes this fee -- see maintenance log." };

        var result = await controller.CreateCharge(property.Id, request, CancellationToken.None);

        var response = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Equal("Tenant disputes this fee -- see maintenance log.", response.Notes);
    }

    [Fact]
    public async Task CreateCharge_NotesDefaultsToNull_WhenOmitted()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);

        var result = await controller.CreateCharge(property.Id, NewRequest(), CancellationToken.None);

        var response = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(result).Value);
        Assert.Null(response.Notes);
    }

    [Fact]
    public async Task CreateCharge_ThrowsValidationException_WhenNotesExceeds500Characters()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var request = NewRequest() with { Notes = new string('x', 501) };

        await Assert.ThrowsAsync<ValidationException>(() => controller.CreateCharge(
            property.Id, request, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateCharge_UpdatesNotes_WhenUnlocked()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateCharge(property.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.UpdateCharge(property.Id, id, NewRequest() with { Notes = "Updated context" }, CancellationToken.None);

        var response = Assert.IsType<ChargeResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("Updated context", response.Notes);
    }

    [Fact]
    public async Task CreateCharge_ThrowsValidationException_WhenDescriptionIsMissing()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var request = NewRequest() with { Description = "" };

        await Assert.ThrowsAsync<ValidationException>(() => controller.CreateCharge(
            property.Id, request, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task CreateCharge_ThrowsValidationException_WhenAmountIsNotPositive(decimal amount)
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var request = NewRequest() with { Amount = amount };

        await Assert.ThrowsAsync<ValidationException>(() => controller.CreateCharge(
            property.Id, request, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateCharge_UpdatesFieldsDirectly_WhenUnlocked()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateCharge(property.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.UpdateCharge(
            property.Id, id, NewRequest() with { Amount = 100m }, CancellationToken.None);

        var response = Assert.IsType<ChargeResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(100m, response.Amount);
    }

    [Fact]
    public async Task UpdateCharge_ThrowsConflict_OnceAPaymentHasBeenAllocated()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateCharge(property.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;
        await AllocatePaymentAsync(db, id, 25m);

        await Assert.ThrowsAsync<ConflictException>(() => controller.UpdateCharge(
            property.Id, id, NewRequest() with { Amount = 999m }, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteCharge_SoftDeletes_WhenUnlocked()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateCharge(property.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.DeleteCharge(property.Id, id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(0, await db.Charges.CountAsync());
        Assert.Equal(1, await db.Charges.IgnoreQueryFilters().CountAsync(c => c.IsDeleted));
    }

    [Fact]
    public async Task DeleteCharge_ThrowsConflict_OnceAPaymentHasBeenAllocated()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateCharge(property.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;
        await AllocatePaymentAsync(db, id, 75m);

        await Assert.ThrowsAsync<ConflictException>(() => controller.DeleteCharge(property.Id, id, CancellationToken.None));
    }

    [Fact]
    public async Task GetCharge_ReflectsPartialPaymentStatus()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateCharge(property.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;
        await AllocatePaymentAsync(db, id, 25m);

        var result = await controller.GetCharge(property.Id, id, CancellationToken.None);

        var response = Assert.IsType<ChargeResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(ChargePaymentStatus.Partial, response.PaymentStatus);
        Assert.Equal(25m, response.AllocatedAmount);
        Assert.Equal(50m, response.OutstandingAmount);
        Assert.True(response.IsLocked);
    }

    [Fact]
    public async Task GetCharge_ReflectsFullyPaidStatus()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateCharge(property.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;
        await AllocatePaymentAsync(db, id, 75m);

        var result = await controller.GetCharge(property.Id, id, CancellationToken.None);

        var response = Assert.IsType<ChargeResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(ChargePaymentStatus.Paid, response.PaymentStatus);
        Assert.Equal(0m, response.OutstandingAmount);
    }

    [Fact]
    public async Task GetCharges_ReturnsOnlyThisPropertysCharges()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var propertyA = await SeedPropertyAsync(db);
        var propertyB = await SeedPropertyAsync(db);
        await controller.CreateCharge(propertyA.Id, NewRequest(), CancellationToken.None);
        await controller.CreateCharge(propertyB.Id, NewRequest(), CancellationToken.None);

        var result = await controller.GetCharges(propertyA.Id, CancellationToken.None);

        var charges = Assert.IsAssignableFrom<IReadOnlyList<ChargeResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        var charge = Assert.Single(charges);
        Assert.Equal(propertyA.Id, charge.PropertyId);
    }

    [Fact]
    public async Task GetCharge_ThrowsNotFound_WhenChargeBelongsToADifferentProperty()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var propertyA = await SeedPropertyAsync(db);
        var propertyB = await SeedPropertyAsync(db);
        var created = await controller.CreateCharge(propertyA.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        await Assert.ThrowsAsync<NotFoundException>(() => controller.GetCharge(propertyB.Id, id, CancellationToken.None));
    }

    [Fact]
    public async Task GetStatement_ComputesDynamicBalance_FromChargesAndPayments()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var rentCreated = await controller.CreateCharge(property.Id, NewRequest(ChargeCategory.BaseRent) with { Amount = 1450m }, CancellationToken.None);
        var rentId = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(rentCreated).Value).Id;
        await controller.CreateCharge(property.Id, NewRequest(ChargeCategory.LateFee) with { Amount = 50m }, CancellationToken.None);
        await AllocatePaymentAsync(db, rentId, 1000m);

        var result = await controller.GetStatement(property.Id, CancellationToken.None);

        var statement = Assert.IsType<UnitStatementResponse>(Assert.IsType<OkObjectResult>(result).Value);
        // Balance = (1450 + 50 charges) - 1000 paid = 500.
        Assert.Equal(500m, statement.Balance);
        Assert.Equal(2, statement.Charges.Count);
        Assert.Single(statement.Payments);
        var rentLine = statement.Charges.Single(c => c.Charge.Id == rentId);
        Assert.Equal(ChargePaymentStatus.Partial, rentLine.Charge.PaymentStatus);
    }

    [Fact]
    public async Task GetStatement_VoidedChargesDoNotCountTowardBalance()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateCharge(property.Id, NewRequest() with { Amount = 200m }, CancellationToken.None);
        var id = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;
        var stored = await db.Charges.SingleAsync(c => c.Id == id);
        stored.Status = ChargeLifecycleStatus.Voided;
        await db.SaveChangesAsync();

        var result = await controller.GetStatement(property.Id, CancellationToken.None);

        var statement = Assert.IsType<UnitStatementResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(0m, statement.Balance);
    }

    [Fact]
    public async Task GetStatement_TransactionLines_AreChronologicalWithRunningBalance()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        // Due/paid out of insertion order on purpose -- proves the timeline sorts by date,
        // not creation order.
        var septemberRentCreated = await controller.CreateCharge(
            property.Id, NewRequest(ChargeCategory.BaseRent) with { Amount = 1000m, DueDate = new DateOnly(2026, 9, 1) }, CancellationToken.None);
        var septemberRentId = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(septemberRentCreated).Value).Id;
        var augustRentCreated = await controller.CreateCharge(
            property.Id, NewRequest(ChargeCategory.BaseRent) with { Amount = 1000m, DueDate = new DateOnly(2026, 8, 1) }, CancellationToken.None);
        var augustRentId = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(augustRentCreated).Value).Id;
        await AllocatePaymentAsync(db, augustRentId, 1000m, new DateOnly(2026, 8, 5));

        var result = await controller.GetStatement(property.Id, CancellationToken.None);

        var statement = Assert.IsType<UnitStatementResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(3, statement.TransactionLines.Count);
        // Aug 1 charge (+1000 -> 1000), Aug 5 payment (-1000 -> 0), Sep 1 charge (+1000 -> 1000).
        Assert.Equal(new DateOnly(2026, 8, 1), statement.TransactionLines[0].Date);
        Assert.Equal("Charge", statement.TransactionLines[0].Type);
        Assert.Equal(augustRentId, statement.TransactionLines[0].ReferenceId);
        Assert.Equal(1000m, statement.TransactionLines[0].RunningBalance);

        Assert.Equal(new DateOnly(2026, 8, 5), statement.TransactionLines[1].Date);
        Assert.Equal("Payment", statement.TransactionLines[1].Type);
        Assert.Equal(0m, statement.TransactionLines[1].RunningBalance);

        Assert.Equal(new DateOnly(2026, 9, 1), statement.TransactionLines[2].Date);
        Assert.Equal("Charge", statement.TransactionLines[2].Type);
        Assert.Equal(septemberRentId, statement.TransactionLines[2].ReferenceId);
        Assert.Equal(1000m, statement.TransactionLines[2].RunningBalance);

        // Matches the final aggregate Balance exactly at the last line.
        Assert.Equal(statement.Balance, statement.TransactionLines[^1].RunningBalance);
    }

    [Fact]
    public async Task GetStatement_TransactionLines_ExcludeVoidedCharges()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateCharge(property.Id, NewRequest() with { Amount = 200m }, CancellationToken.None);
        var id = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;
        var stored = await db.Charges.SingleAsync(c => c.Id == id);
        stored.Status = ChargeLifecycleStatus.Voided;
        await db.SaveChangesAsync();

        var result = await controller.GetStatement(property.Id, CancellationToken.None);

        var statement = Assert.IsType<UnitStatementResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(statement.TransactionLines);
    }

    [Fact]
    public async Task VoidCharge_MarksVoided_WhenUnlocked()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateCharge(property.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.VoidCharge(property.Id, id, CancellationToken.None);

        var response = Assert.IsType<ChargeResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(ChargeLifecycleStatus.Voided, response.Status);
        Assert.Equal(0m, response.OutstandingAmount);
    }

    [Fact]
    public async Task VoidCharge_ThrowsConflict_OnceAPaymentHasBeenAllocated()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateCharge(property.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;
        await AllocatePaymentAsync(db, id, 25m);

        await Assert.ThrowsAsync<ConflictException>(() => controller.VoidCharge(property.Id, id, CancellationToken.None));
    }

    [Fact]
    public async Task VoidCharge_ThrowsConflict_WhenAlreadyVoided()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateCharge(property.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;
        await controller.VoidCharge(property.Id, id, CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => controller.VoidCharge(property.Id, id, CancellationToken.None));
    }

    [Fact]
    public async Task CreateChargeAdjustment_ReducesOutstandingAmount_OnALockedCharge()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateCharge(property.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;
        await AllocatePaymentAsync(db, id, 25m); // locks the charge -- Update/Delete/Void would now throw

        var result = await controller.CreateChargeAdjustment(
            property.Id, id, new CreateChargeAdjustmentRequest(AdjustmentType.CreditAdjustment, 50m, "Goodwill credit for late maintenance"),
            CancellationToken.None);

        var created201 = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(201, created201.StatusCode);
        var adjustmentResponse = Assert.IsType<ChargeAdjustmentResponse>(created201.Value);
        Assert.Equal(50m, adjustmentResponse.Amount);

        var chargeAfter = Assert.IsType<ChargeResponse>(Assert.IsType<OkObjectResult>(
            await controller.GetCharge(property.Id, id, CancellationToken.None)).Value);
        // Amount(75) - CreditAdjustment(50) - Allocated(25) = 0 outstanding.
        Assert.Equal(0m, chargeAfter.OutstandingAmount);
        Assert.Equal(ChargePaymentStatus.Paid, chargeAfter.PaymentStatus);
    }

    [Fact]
    public async Task CreateChargeAdjustment_ThrowsValidationException_WhenReasonIsMissing()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateCharge(property.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        await Assert.ThrowsAsync<ValidationException>(() => controller.CreateChargeAdjustment(
            property.Id, id, new CreateChargeAdjustmentRequest(AdjustmentType.DebitAdjustment, 10m, ""), CancellationToken.None));
    }

    [Fact]
    public async Task CreateChargeAdjustment_ThrowsValidationException_WhenAmountIsNotPositive()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateCharge(property.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        await Assert.ThrowsAsync<ValidationException>(() => controller.CreateChargeAdjustment(
            property.Id, id, new CreateChargeAdjustmentRequest(AdjustmentType.DebitAdjustment, 0m, "Late fee correction"), CancellationToken.None));
    }

    [Fact]
    public async Task CreateChargeAdjustment_DebitIncreasesOutstandingAmount_OnAnUnlockedCharge()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var created = await controller.CreateCharge(property.Id, NewRequest(), CancellationToken.None);
        var id = Assert.IsType<ChargeResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        await controller.CreateChargeAdjustment(
            property.Id, id, new CreateChargeAdjustmentRequest(AdjustmentType.DebitAdjustment, 15m, "Additional cleaning cost found"),
            CancellationToken.None);

        var chargeAfter = Assert.IsType<ChargeResponse>(Assert.IsType<OkObjectResult>(
            await controller.GetCharge(property.Id, id, CancellationToken.None)).Value);
        Assert.Equal(90m, chargeAfter.OutstandingAmount); // 75 + 15 debit, still unpaid
    }

    [Theory]
    [InlineData(StatementDateRange.Lifetime)]
    [InlineData(StatementDateRange.YearToDate)]
    [InlineData(StatementDateRange.Last12Months)]
    public async Task GetStatementPdf_ReturnsANonEmptyPdf_ForEveryDateRange(StatementDateRange range)
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        await controller.CreateCharge(property.Id, NewRequest(), CancellationToken.None);

        var result = await controller.GetStatementPdf(property.Id, range, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.True(file.FileContents.Length > 0);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(file.FileContents, 0, 4));
    }

    [Fact]
    public async Task GetStatementPdf_ThrowsNotFound_WhenPropertyDoesNotExist()
    {
        var (db, controller) = CreateController(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() => controller.GetStatementPdf(
            Guid.NewGuid(), StatementDateRange.Lifetime, CancellationToken.None));
    }
}
