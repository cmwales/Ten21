using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.UnitGroups;
using Ten21.Api.Controllers;
using Ten21.Domain.Entities;
using Ten21.Domain.Enums;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Ten21.Infrastructure.Security;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>US-29: workspace-scoped physical section/phase catalog CRUD, same in-memory
/// SQLite pattern as PropertiesControllerTests.</summary>
public class UnitGroupsControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HtmlInputSanitizer _sanitizer = new();

    public UnitGroupsControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, UnitGroupsController Controller) CreateController(Guid tenantId)
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

        return (db, new UnitGroupsController(db, _sanitizer));
    }

    private static UpsertUnitGroupRequest NewRequest() => new(
        GroupName: "North Wing",
        Description: "Phase 1 construction, completed 2019.");

    [Fact]
    public async Task CreateUnitGroup_Persists_AndReturnsResponse()
    {
        var (db, controller) = CreateController(Guid.NewGuid());

        var result = await controller.CreateUnitGroup(NewRequest(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var response = Assert.IsType<UnitGroupResponse>(created.Value);
        Assert.Equal("North Wing", response.GroupName);
        Assert.Equal(1, await db.UnitGroups.CountAsync());
    }

    [Fact]
    public async Task CreateUnitGroup_ThrowsValidationException_WhenGroupNameIsMissing()
    {
        var (_, controller) = CreateController(Guid.NewGuid());
        var request = NewRequest() with { GroupName = "" };

        await Assert.ThrowsAsync<ValidationException>(
            () => controller.CreateUnitGroup(request, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateUnitGroup_UpdatesFieldsDirectly()
    {
        var (_, controller) = CreateController(Guid.NewGuid());
        var created = await controller.CreateUnitGroup(NewRequest(), CancellationToken.None);
        var id = Assert.IsType<UnitGroupResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.UpdateUnitGroup(id, NewRequest() with { GroupName = "South Wing" }, CancellationToken.None);

        var response = Assert.IsType<UnitGroupResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("South Wing", response.GroupName);
    }

    [Fact]
    public async Task GetUnitGroups_ReturnsOnlyActiveTenantsGroups()
    {
        var tenantId = Guid.NewGuid();
        var (_, controllerA) = CreateController(tenantId);
        await controllerA.CreateUnitGroup(NewRequest(), CancellationToken.None);

        var (_, controllerOtherTenant) = CreateController(Guid.NewGuid());
        await controllerOtherTenant.CreateUnitGroup(NewRequest() with { GroupName = "Other Tenant Group" }, CancellationToken.None);

        var (_, controllerB) = CreateController(tenantId);
        var result = await controllerB.GetUnitGroups(CancellationToken.None);

        var groups = Assert.IsAssignableFrom<IReadOnlyList<UnitGroupResponse>>(Assert.IsType<OkObjectResult>(result).Value);
        var group = Assert.Single(groups);
        Assert.Equal("North Wing", group.GroupName);
    }

    [Fact]
    public async Task DeleteUnitGroup_Succeeds_WhenNoPropertyReferencesIt()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var created = await controller.CreateUnitGroup(NewRequest(), CancellationToken.None);
        var id = Assert.IsType<UnitGroupResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var result = await controller.DeleteUnitGroup(id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(0, await db.UnitGroups.CountAsync());
    }

    [Fact]
    public async Task DeleteUnitGroup_ThrowsConflict_WhenAssignedToAProperty()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        var created = await controller.CreateUnitGroup(NewRequest(), CancellationToken.None);
        var id = Assert.IsType<UnitGroupResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

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
            UnitGroupId = id,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => controller.DeleteUnitGroup(id, CancellationToken.None));
        Assert.Equal(1, await db.UnitGroups.CountAsync());
    }

    [Fact]
    public async Task GetUnitGroup_ThrowsNotFound_ForAnotherTenantsGroup()
    {
        var (_, controllerA) = CreateController(Guid.NewGuid());
        var created = await controllerA.CreateUnitGroup(NewRequest(), CancellationToken.None);
        var id = Assert.IsType<UnitGroupResponse>(Assert.IsType<CreatedAtActionResult>(created).Value).Id;

        var (_, controllerB) = CreateController(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(() => controllerB.GetUnitGroup(id, CancellationToken.None));
    }
}
