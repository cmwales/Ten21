using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Workspace;
using Ten21.Api.Controllers;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-36: the workspace-wide ledger rollup -- a pure reporting aggregation, no new
/// tables, over the same Charge/PaymentTransaction/ChargeAdjustment rows the per-property
/// unit statement (US-33) already reads. Same in-memory SQLite pattern as
/// ChargesControllerTests.</summary>
public class WorkspaceLedgerControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public WorkspaceLedgerControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, WorkspaceLedgerController Controller) CreateController(Guid tenantId)
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

        return (db, new WorkspaceLedgerController(db));
    }

    private static async Task<Property> SeedPropertyAsync(Ten21DbContext db, string name, string? unitIdentifier = null)
    {
        var property = new Property
        {
            Id = Guid.NewGuid(),
            Name = name,
            PropertyType = PropertyType.MultiFamily,
            StreetAddress1 = "100 Main St",
            City = "Provo",
            State = "UT",
            PostalCode = "84601",
            Country = "USA",
            UnitIdentifier = unitIdentifier,
            OccupancyStatus = OccupancyStatus.Occupied,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Properties.Add(property);
        await db.SaveChangesAsync();
        return property;
    }

    private static async Task<Charge> SeedChargeAsync(
        Ten21DbContext db, Guid propertyId, decimal amount, ChargeLifecycleStatus status = ChargeLifecycleStatus.Active)
    {
        var charge = new Charge
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            Description = "September Rent",
            Amount = amount,
            DueDate = new DateOnly(2026, 9, 1),
            Category = ChargeCategory.BaseRent,
            AllocationPriority = Charge.DefaultAllocationPriorityFor(ChargeCategory.BaseRent),
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Charges.Add(charge);
        await db.SaveChangesAsync();
        return charge;
    }

    private static async Task SeedPaymentAsync(Ten21DbContext db, Guid propertyId, Guid chargeId, decimal amount)
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
            PaymentDate = new DateOnly(2026, 9, 2),
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
    public async Task GetWorkspaceLedger_ReturnsZeroBalance_WhenNoPropertiesExist()
    {
        var (_, controller) = CreateController(Guid.NewGuid());

        var result = await controller.GetWorkspaceLedger(CancellationToken.None);

        var response = Assert.IsType<WorkspaceLedgerResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(0m, response.TotalBalance);
        Assert.Empty(response.Properties);
    }

    [Fact]
    public async Task GetWorkspaceLedger_SumsBalancesAcrossMultipleProperties()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var propertyA = await SeedPropertyAsync(db, "Riverside A", "Unit 1");
        var propertyB = await SeedPropertyAsync(db, "Riverside B", "Unit 2");
        await SeedChargeAsync(db, propertyA.Id, 1000m);
        await SeedChargeAsync(db, propertyB.Id, 500m);

        var result = await controller.GetWorkspaceLedger(CancellationToken.None);

        var response = Assert.IsType<WorkspaceLedgerResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(1500m, response.TotalBalance);
        Assert.Equal(2, response.Properties.Count);
        Assert.Equal(1000m, response.Properties.Single(p => p.PropertyId == propertyA.Id).Balance);
        Assert.Equal(500m, response.Properties.Single(p => p.PropertyId == propertyB.Id).Balance);
        Assert.Equal("Unit 1", response.Properties.Single(p => p.PropertyId == propertyA.Id).UnitIdentifier);
    }

    [Fact]
    public async Task GetWorkspaceLedger_AccountsForPaymentsAndAdjustments()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db, "Riverside A");
        var charge = await SeedChargeAsync(db, property.Id, 1000m);
        await SeedPaymentAsync(db, property.Id, charge.Id, 400m);
        db.ChargeAdjustments.Add(new ChargeAdjustment
        {
            Id = Guid.NewGuid(),
            TargetChargeId = charge.Id,
            AdjustmentType = AdjustmentType.CreditAdjustment,
            Amount = 100m,
            Reason = "Goodwill credit",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await controller.GetWorkspaceLedger(CancellationToken.None);

        var response = Assert.IsType<WorkspaceLedgerResponse>(Assert.IsType<OkObjectResult>(result).Value);
        // 1000 charge - 400 paid - 100 credit = 500.
        Assert.Equal(500m, Assert.Single(response.Properties).Balance);
        Assert.Equal(500m, response.TotalBalance);
    }

    [Fact]
    public async Task GetWorkspaceLedger_ExcludesVoidedCharges()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var property = await SeedPropertyAsync(db, "Riverside A");
        await SeedChargeAsync(db, property.Id, 1000m, ChargeLifecycleStatus.Voided);

        var result = await controller.GetWorkspaceLedger(CancellationToken.None);

        var response = Assert.IsType<WorkspaceLedgerResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(0m, response.TotalBalance);
        Assert.Equal(0m, Assert.Single(response.Properties).Balance);
    }

    [Fact]
    public async Task GetWorkspaceLedger_OnlyIncludesTheCallersOwnTenant()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        await SeedPropertyAsync(db, "My Property");

        var (_, otherTenantController) = CreateController(Guid.NewGuid());

        var result = await otherTenantController.GetWorkspaceLedger(CancellationToken.None);

        var response = Assert.IsType<WorkspaceLedgerResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(response.Properties);
    }
}
