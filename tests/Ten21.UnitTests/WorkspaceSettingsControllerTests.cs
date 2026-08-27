using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Workspace;
using Ten21.Api.Controllers;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>Refinement Sprint (Directive 4): the /admin/settings backend -- a single
/// WorkspaceSettings row per tenant, lazily created on first read. Same in-memory SQLite
/// pattern as the other controller test classes.</summary>
public class WorkspaceSettingsControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public WorkspaceSettingsControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, WorkspaceSettingsController Controller) CreateController(Guid tenantId)
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

        return (db, new WorkspaceSettingsController(db));
    }

    [Fact]
    public async Task GetSettings_CreatesDefaultRow_EnabledByDefault_WhenNoneExists()
    {
        var (db, controller) = CreateController(Guid.NewGuid());

        var result = await controller.GetSettings(CancellationToken.None);

        var response = Assert.IsType<WorkspaceSettingsResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(response.EnableCommunityDirectory);
        Assert.Equal(1, await db.WorkspaceSettings.CountAsync());
    }

    [Fact]
    public async Task GetSettings_DoesNotCreateASecondRow_OnRepeatedCalls()
    {
        var (db, controller) = CreateController(Guid.NewGuid());

        await controller.GetSettings(CancellationToken.None);
        await controller.GetSettings(CancellationToken.None);

        Assert.Equal(1, await db.WorkspaceSettings.CountAsync());
    }

    [Fact]
    public async Task UpdateSettings_PersistsDisabledState()
    {
        var (db, controller) = CreateController(Guid.NewGuid());
        await controller.GetSettings(CancellationToken.None);

        var updateResult = await controller.UpdateSettings(new UpdateWorkspaceSettingsRequest(false), CancellationToken.None);

        var updateResponse = Assert.IsType<WorkspaceSettingsResponse>(Assert.IsType<OkObjectResult>(updateResult).Value);
        Assert.False(updateResponse.EnableCommunityDirectory);

        var getResult = await controller.GetSettings(CancellationToken.None);
        var getResponse = Assert.IsType<WorkspaceSettingsResponse>(Assert.IsType<OkObjectResult>(getResult).Value);
        Assert.False(getResponse.EnableCommunityDirectory);
        Assert.Equal(1, await db.WorkspaceSettings.CountAsync());
    }

    [Fact]
    public async Task UpdateSettings_IsScopedPerTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var (_, controllerA) = CreateController(tenantA);
        var (_, controllerB) = CreateController(tenantB);

        await controllerA.UpdateSettings(new UpdateWorkspaceSettingsRequest(false), CancellationToken.None);

        var resultB = await controllerB.GetSettings(CancellationToken.None);
        var responseB = Assert.IsType<WorkspaceSettingsResponse>(Assert.IsType<OkObjectResult>(resultB).Value);
        Assert.True(responseB.EnableCommunityDirectory);
    }
}
