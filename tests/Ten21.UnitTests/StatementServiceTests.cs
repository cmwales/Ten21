using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Application.Abstractions;
using Ten21.Business.Charges;
using Ten21.Business.Payments;
using Ten21.Business.Statements;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Infrastructure.Pdf;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Ten21.Infrastructure.Security;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>
/// Business-layer refactor: exercises StatementService directly, with no ChargesController/
/// HTTP layer involved -- same rationale as ChargeServiceTests/PaymentServiceTests. Full
/// end-to-end coverage still lives in ChargesControllerTests.cs (GetStatement tests).
/// </summary>
public class StatementServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HtmlInputSanitizer _sanitizer = new();
    private readonly IPdfService _pdfService = new QuestPdfService();

    static StatementServiceTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    public StatementServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, ChargeService Charges, PaymentService Payments, StatementService Statements) CreateServices(Guid tenantId)
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

        var chargeService = new ChargeService(db, new ChargeRepository(db), _sanitizer);
        return (
            db,
            chargeService,
            new PaymentService(db, new PaymentRepository(db), _sanitizer),
            new StatementService(new StatementRepository(db), chargeService, _pdfService));
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

    [Fact]
    public async Task BuildStatementAsync_ComputesPositiveBalance_ForAnUnpaidCharge()
    {
        var (db, charges, _, statements) = CreateServices(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        await charges.CreateAsync(
            property.Id,
            new UpsertChargeRequest("September Rent", 1000m, new DateOnly(2026, 9, 1), null, ChargeCategory.BaseRent),
            CancellationToken.None);

        var statement = await statements.BuildStatementAsync(property.Id, CancellationToken.None);

        Assert.Equal(1000m, statement.Balance);
        Assert.Equal(0m, statement.AvailableCredit);
        Assert.Single(statement.Charges);
    }

    [Fact]
    public async Task BuildStatementAsync_ReflectsPaymentAgainstBalance_AndTracksAvailableCredit()
    {
        var (db, charges, payments, statements) = CreateServices(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        var resident = await SeedResidentAsync(db, property.Id);
        await charges.CreateAsync(
            property.Id,
            new UpsertChargeRequest("September Rent", 1000m, new DateOnly(2026, 9, 1), null, ChargeCategory.BaseRent),
            CancellationToken.None);
        await payments.LogPaymentAsync(
            property.Id,
            new LogPaymentRequest(resident.Id, new DateOnly(2026, 9, 2), 1200m, TenderType.Check, null, null),
            CancellationToken.None);

        var statement = await statements.BuildStatementAsync(property.Id, CancellationToken.None);

        // Overpayment drives Balance negative (a credit), and the 200 leftover is tracked
        // separately as AvailableCredit -- see UnitStatementResponse's own comment.
        Assert.Equal(-200m, statement.Balance);
        Assert.Equal(200m, statement.AvailableCredit);
        Assert.Single(statement.Payments);
    }

    [Fact]
    public async Task BuildStatementPdfAsync_ProducesNonEmptyPdfBytes()
    {
        var (db, charges, _, statements) = CreateServices(Guid.NewGuid());
        var property = await SeedPropertyAsync(db);
        await charges.CreateAsync(
            property.Id,
            new UpsertChargeRequest("September Rent", 1000m, new DateOnly(2026, 9, 1), null, ChargeCategory.BaseRent),
            CancellationToken.None);

        var pdfBytes = await statements.BuildStatementPdfAsync(property.Id, StatementDateRange.Lifetime, CancellationToken.None);

        Assert.NotEmpty(pdfBytes);
    }
}
