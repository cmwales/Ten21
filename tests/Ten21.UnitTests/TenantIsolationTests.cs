using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ten21.Domain.Entities;
using Ten21.Infrastructure.Persistence;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>
/// Proves the US-01 acceptance criteria using an in-memory SQLite database (fast, no
/// external dependencies). This validates the EF Core query-filter + write-stamping half of
/// isolation -- the exact behaviors called out in the acceptance criteria. The Postgres RLS
/// half (TenantSessionInterceptor + sql/rls-policies.sql) requires a real Postgres instance
/// and belongs in Ten21.IntegrationTests once Docker/Testcontainers is available.
/// </summary>
public class TenantIsolationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public TenantIsolationTests()
    {
        // A fresh in-memory connection per test instance (xUnit creates a new class
        // instance per test) -- schema and data never leak between tests.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private Ten21DbContext CreateContext(TenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<Ten21DbContext>()
            .UseSqlite(_connection)
            .Options;

        var context = new Ten21DbContext(options, tenantContext);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task Query_ReturnsOnlyActiveTenantsProperties()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var seedContextA = new TenantContext();
        seedContextA.SetTenant(tenantA);
        using (var seedDb = CreateContext(seedContextA))
        {
            seedDb.Properties.Add(NewProperty("1 Tenant A Way", "Salt Lake City"));
            await seedDb.SaveChangesAsync();
        }

        var seedContextB = new TenantContext();
        seedContextB.SetTenant(tenantB);
        using (var seedDb = CreateContext(seedContextB))
        {
            seedDb.Properties.Add(NewProperty("1 Tenant B Way", "Orlando"));
            await seedDb.SaveChangesAsync();
        }

        var readContextA = new TenantContext();
        readContextA.SetTenant(tenantA);
        using var readDb = CreateContext(readContextA);

        var results = await readDb.Properties.ToListAsync();

        Assert.Single(results);
        Assert.Equal("1 Tenant A Way", results[0].StreetAddress1);
    }

    [Fact]
    public async Task Insert_AutoStampsActiveTenantId()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId);

        using var db = CreateContext(tenantContext);
        var property = NewProperty("42 Auto Stamp Ln", "Layton");

        db.Properties.Add(property);
        await db.SaveChangesAsync();

        Assert.Equal(tenantId, property.TenantId);
    }

    [Fact]
    public async Task Insert_ThrowsWhenNoTenantContextIsResolved()
    {
        var tenantContext = new TenantContext(); // SetTenant deliberately never called
        using var db = CreateContext(tenantContext);

        db.Properties.Add(NewProperty("No Tenant St", "Nowhere"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Query_ReturnsZeroRows_WhenTenantContextUnresolved()
    {
        var tenantId = Guid.NewGuid();
        var seedContext = new TenantContext();
        seedContext.SetTenant(tenantId);
        using (var seedDb = CreateContext(seedContext))
        {
            seedDb.Properties.Add(NewProperty("1 Somewhere Ave", "Ogden"));
            await seedDb.SaveChangesAsync();
        }

        var unresolvedContext = new TenantContext(); // fail-closed: no SetTenant call
        using var readDb = CreateContext(unresolvedContext);

        var results = await readDb.Properties.ToListAsync();

        Assert.Empty(results);
    }

    [Fact]
    public void SetTenant_ThrowsIfCalledTwiceInSameScope()
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => tenantContext.SetTenant(Guid.NewGuid()));
    }

    private static Property NewProperty(string streetAddress, string city) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Tenant Isolation Test Property",
        StreetAddress1 = streetAddress,
        City = city,
        State = "UT",
        PostalCode = "84000",
        Country = "USA",
        CreatedAt = DateTimeOffset.UtcNow
    };
}
