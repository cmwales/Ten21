using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.UnitTiers;
using Ten21.Api.Controllers;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Ten21.Infrastructure.Security;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-29: workspace-scoped pricing tier catalog CRUD, same in-memory SQLite pattern
/// as PropertiesControllerTests.</summary>
public class UnitTiersControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HtmlInputSanitizer _sanitizer = new();

    public UnitTiersControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, UnitTiersController Controller) CreateController(Guid tenantId)
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

        return (db, new UnitTiersController(db, _sanitizer));
    }

    private static UpsertUnitTierRequest NewRequest() => new(
        TierName: "Ocean View 2BR",
        DefaultRent: 2200m,
        AccountingCode: "GL-4010-PREM",
        Description: "2nd floor, ocean-facing units.");

    [Fact]
    public async Task CreateUnitTier_Persists_AndReturnsResponse()
    {
        var (db, controller) = CreateController(Guid.NewGuid());

        var result = await controller.CreateUnitTier(NewRequest(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<UnitTierResponse>(created.Value);
        Assert.Equal("Ocean View 2BR", response.TierName);
        Assert.Equal(2200m, response.DefaultRent);
        Assert.Equal(1, await db.UnitTiers.CountAsync());
    }

    [Fact]
    public async Task CreateUnitTier_ThrowsValidationException_WhenTierNameIsMissing()
    {
        var (_, controller) = CreateController(Guid.NewGuid());
        var request = NewRequest() with { TierName = "" };

        await Assert.ThrowsAsync<ValidationException>(
            () => controller.CreateUnitTier(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateUnitTier_ThrowsValidationException_WhenDefaultRentIsNegative()
    {
        var (_, controller) = CreateController(Guid.NewGuid());
        var request = NewRequest() with { DefaultRent = -1m };

        await Assert.ThrowsAsync<ValidationException>(
            () => controller.CreateUnitTier(request, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateUnitTier_UpdatesFieldsDirectly()
    {
        var (_, controller) = CreateController(Guid.NewGuid());
        var created = await controller.CreateUnitTier(NewRequest(), CancellationToken.None);
        var id = Assert.IsType<UnitTierResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.UpdateUnitTier(id, NewRequest() with { DefaultRent = 2500m }, CancellationToken.None);

        var response = Assert.IsType<UnitTierResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(2500m, response.DefaultRent);
    }

    [Fact]
    public async Task GetUnitTiers_ReturnsOnlyActiveTenantsTiers()
    {
        var tenantId = Guid.NewGuid();
        var (_, controllerA) = CreateController(tenantId);
        await controllerA.CreateUnitTier(NewRequest(), CancellationToken.None);

        var (_, controllerOtherTenant) = CreateController(Guid.NewGuid());
        await controllerOtherTenant.CreateUnitTier(NewRequest() with { TierName = "Other Tenant Tier" }, CancellationToken.None);

        var (_, controllerB) = CreateController(tenantId);
        var result = await controllerB.GetUnitTiers(CancellationToken.None);

        var tiers = Assert.IsAssignableFrom<IReadOnlyList<UnitTierResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        var tier = Assert.Single(tiers);
        Assert.Equal("Ocean View 2BR", tier.TierName);
    }

    [Fact]
    public async Task DeleteUnitTier_Succeeds_WhenNoPropertyReferencesIt()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var created = await controller.CreateUnitTier(NewRequest(), CancellationToken.None);
        var id = Assert.IsType<UnitTierResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.DeleteUnitTier(id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(0, await db.UnitTiers.CountAsync());
    }

    [Fact]
    public async Task DeleteUnitTier_ThrowsConflict_WhenAssignedToAProperty()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var created = await controller.CreateUnitTier(NewRequest(), CancellationToken.None);
        var id = Assert.IsType<UnitTierResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        db.Properties.Add(new Property
        {
            Id = Guid.NewGuid(),
            Name = "Riverside Apartments",
            PropertyType = PropertyType.MultiFamily,
            StreetAddress1 = "100 Main St",
            City = "Provo",
            State = "UT",
            PostalCode = "84601",
            Country = "USA",
            OccupancyStatus = OccupancyStatus.Vacant,
            UnitTierId = id,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => controller.DeleteUnitTier(id, CancellationToken.None));
        Assert.Equal(1, await db.UnitTiers.CountAsync());
    }

    [Fact]
    public async Task GetUnitTier_ThrowsNotFound_ForAnotherTenantsTier()
    {
        var (_, controllerA) = CreateController(Guid.NewGuid());
        var created = await controllerA.CreateUnitTier(NewRequest(), CancellationToken.None);
        var id = Assert.IsType<UnitTierResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var (_, controllerB) = CreateController(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() => controllerB.GetUnitTier(id, CancellationToken.None));
    }
}
