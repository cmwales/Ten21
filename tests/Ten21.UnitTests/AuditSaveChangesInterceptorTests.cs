using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Domain.Entities;
using Ten21.Infrastructure.Persistence;
using Ten21.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>
/// Property is both ITenantScopedEntity and IAuditableEntity/ISoftDelete, making it the
/// one entity that exercises every combination -- same reasoning as reusing it for the
/// US-01 tests.
/// </summary>
public class AuditSaveChangesInterceptorTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public AuditSaveChangesInterceptorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private (Ten21DbContext Db, TenantContext TenantContext) CreateContext(Guid tenantId, Guid? userId = null)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId);
        if (userId is not null)
        {
            tenantContext.SetUser(userId.Value);
        }

        var options = new DbContextOptionsBuilder<Ten21DbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(tenantContext, new HardDeleteOverride()))
            .Options;

        var db = new Ten21DbContext(options, tenantContext);
        db.Database.EnsureCreated();
        return (db, tenantContext);
    }

    private static Property NewProperty() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Audit Test Property",
        StreetAddress1 = "1 Audit Test Way",
        City = "Provo",
        State = "UT",
        PostalCode = "84601",
        Country = "USA",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Insert_CreatesAnAuditLogRow()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (db, _) = CreateContext(tenantId, userId);

        var property = NewProperty();
        db.Properties.Add(property);
        await db.SaveChangesAsync();

        var auditRow = await db.AuditLogs.SingleAsync(a => a.EntityId == property.Id.ToString());
        Assert.Equal("Insert", auditRow.Action);
        Assert.Equal(userId, auditRow.ChangedByUserId);
        Assert.Equal(tenantId, auditRow.TenantId);
        Assert.Null(auditRow.OriginalValuesJson);
        Assert.NotNull(auditRow.NewValuesJson);
        Assert.Contains(property.StreetAddress1, auditRow.NewValuesJson);
    }

    [Fact]
    public async Task Update_CreatesAnAuditLogRowWithBothOriginalAndNewValues()
    {
        var tenantId = Guid.NewGuid();
        var (db, _) = CreateContext(tenantId);

        var property = NewProperty();
        db.Properties.Add(property);
        await db.SaveChangesAsync();

        property.City = "Orem";
        await db.SaveChangesAsync();

        var updateRow = await db.AuditLogs
            .Where(a => a.EntityId == property.Id.ToString() && a.Action == "Update")
            .SingleAsync();

        Assert.Contains("Provo", updateRow.OriginalValuesJson);
        Assert.Contains("Orem", updateRow.NewValuesJson);
    }

    [Fact]
    public async Task Delete_ConvertsToSoftDelete_AndRowStillExistsInDatabase()
    {
        var tenantId = Guid.NewGuid();
        var (db, _) = CreateContext(tenantId);

        var property = NewProperty();
        db.Properties.Add(property);
        await db.SaveChangesAsync();

        db.Properties.Remove(property);
        await db.SaveChangesAsync();

        // Normal query (filter applies): should NOT be visible.
        var visible = await db.Properties.SingleOrDefaultAsync(p => p.Id == property.Id);
        Assert.Null(visible);

        // IgnoreQueryFilters: the row must still physically exist with IsDeleted = true --
        // proving this was a soft delete, not a real DELETE statement.
        var raw = await db.Properties.IgnoreQueryFilters().SingleAsync(p => p.Id == property.Id);
        Assert.True(raw.IsDeleted);
    }

    [Fact]
    public async Task Delete_LogsAsUpdateAction_NotDeleteAction()
    {
        // The interceptor converts EntityState.Deleted -> Modified BEFORE building the
        // audit entry, so the audit trail should reflect what actually happened at the
        // database level (an UPDATE), not the caller's original intent.
        var tenantId = Guid.NewGuid();
        var (db, _) = CreateContext(tenantId);

        var property = NewProperty();
        db.Properties.Add(property);
        await db.SaveChangesAsync();

        db.Properties.Remove(property);
        await db.SaveChangesAsync();

        var auditRows = await db.AuditLogs
            .Where(a => a.EntityId == property.Id.ToString())
            .ToListAsync();

        Assert.Contains(auditRows, a => a.Action == "Update");
        Assert.DoesNotContain(auditRows, a => a.Action == "Delete");
    }

    [Fact]
    public async Task Delete_MarkedForHardDelete_ProducesARealDeleteNotASoftDelete()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId);
        var hardDeleteOverride = new HardDeleteOverride();

        var options = new DbContextOptionsBuilder<Ten21DbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditSaveChangesInterceptor(tenantContext, hardDeleteOverride))
            .Options;
        var db = new Ten21DbContext(options, tenantContext);
        db.Database.EnsureCreated();

        var property = NewProperty();
        db.Properties.Add(property);
        await db.SaveChangesAsync();

        hardDeleteOverride.MarkForHardDelete(property);
        db.Properties.Remove(property);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Properties.IgnoreQueryFilters().Where(p => p.Id == property.Id).ToListAsync());
    }

    [Fact]
    public async Task Insert_StampsCreatedByUserId_AndLeavesUpdatedFieldsNull()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (db, _) = CreateContext(tenantId, userId);

        var property = NewProperty();
        db.Properties.Add(property);
        await db.SaveChangesAsync();

        var saved = await db.Properties.SingleAsync(p => p.Id == property.Id);
        Assert.Equal(userId, saved.CreatedByUserId);
        Assert.Null(saved.UpdatedAt);
        Assert.Null(saved.UpdatedByUserId);
    }

    [Fact]
    public async Task Update_StampsUpdatedAtAndUpdatedByUserId_AndLeavesCreatedByUserIdUnchanged()
    {
        // Two separate DbContext/TenantContext pairs sharing the same Sqlite connection --
        // TenantContext.SetUser is one-shot per scope (mirrors one real HTTP request each),
        // so simulating a different editor requires a second "request" against the same
        // underlying database, not a second SetUser call on the same instance.
        var tenantId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var (db, _) = CreateContext(tenantId, creatorId);

        var property = NewProperty();
        db.Properties.Add(property);
        await db.SaveChangesAsync();

        var editorId = Guid.NewGuid();
        var (editorDb, _) = CreateContext(tenantId, editorId);
        var tracked = await editorDb.Properties.SingleAsync(p => p.Id == property.Id);
        tracked.City = "Orem";
        await editorDb.SaveChangesAsync();

        var saved = await editorDb.Properties.SingleAsync(p => p.Id == property.Id);
        Assert.Equal(creatorId, saved.CreatedByUserId);
        Assert.Equal(editorId, saved.UpdatedByUserId);
        Assert.NotNull(saved.UpdatedAt);
    }

    /// <summary>The soft-delete conversion (Deleted -> Modified) happens before the audit
    /// stamping runs, so a "delete" should stamp UpdatedAt/UpdatedByUserId exactly like any
    /// other update -- it IS one, at the database level.</summary>
    [Fact]
    public async Task SoftDelete_StampsUpdatedAtAndUpdatedByUserId()
    {
        var tenantId = Guid.NewGuid();
        var (db, _) = CreateContext(tenantId, Guid.NewGuid());

        var property = NewProperty();
        db.Properties.Add(property);
        await db.SaveChangesAsync();

        var deleterId = Guid.NewGuid();
        var (deleterDb, _) = CreateContext(tenantId, deleterId);
        var tracked = await deleterDb.Properties.SingleAsync(p => p.Id == property.Id);
        deleterDb.Properties.Remove(tracked);
        await deleterDb.SaveChangesAsync();

        var raw = await deleterDb.Properties.IgnoreQueryFilters().SingleAsync(p => p.Id == property.Id);
        Assert.Equal(deleterId, raw.UpdatedByUserId);
        Assert.NotNull(raw.UpdatedAt);
    }

    [Fact]
    public async Task NonAuditableEntity_DoesNotGenerateAuditRows()
    {
        // Tenant/Organization implement neither IAuditableEntity nor ISoftDelete --
        // inserting one should produce zero AuditLog rows.
        var tenantId = Guid.NewGuid();
        var (db, _) = CreateContext(tenantId);

        db.Organizations.Add(new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Unaudited Org",
            SubscriptionTier = "Starter",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        Assert.Empty(await db.AuditLogs.ToListAsync());
    }
}
