using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ten21.Domain.Common;
using Ten21.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ten21.IntegrationTests;

/// <summary>
/// Proves the one thing tests/Ten21.UnitTests/TenantIsolationTests.cs genuinely cannot:
/// Postgres Row-Level Security (sql/rls-policies.sql, folded into the InitialCreate migration)
/// blocks cross-tenant reads even when the EF Core query filter isn't in play at all -- raw
/// SQL, a separate connection, no Ten21DbContext involved.
///
/// Requires connecting as a non-superuser, non-table-owning role: the Testcontainers default
/// "postgres" role is the initdb superuser, and Postgres always exempts superusers (and any
/// role with BYPASSRLS) from RLS regardless of FORCE ROW LEVEL SECURITY -- see the warning at
/// the top of sql/rls-policies.sql. This creates a low-privilege "ten21_app_test" role and
/// does every tenant-sensitive read/write through it, mirroring how the real app is expected
/// to connect as a non-superuser role in production.
/// </summary>
public class RlsIsolationTests : IAsyncLifetime
{
    private const string AppRolePassword = "Rls-Test-Only-Passw0rd!1";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Schema + RLS policies, via the exact same migration Program.cs applies in
        // Development. The container's default "postgres" role owns everything created here.
        var options = new DbContextOptionsBuilder<Ten21DbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using (var db = new Ten21DbContext(options, new TenantContext()))
        {
            await db.Database.MigrateAsync();
        }

        await using var adminConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await adminConnection.OpenAsync();

        await using (var createRole = adminConnection.CreateCommand())
        {
            // Password is a hardcoded test-only constant, not user input -- no injection
            // concern from inlining it (CREATE ROLE doesn't support parameterized DDL anyway).
            createRole.CommandText = $"CREATE ROLE ten21_app_test LOGIN PASSWORD '{AppRolePassword}';";
            await createRole.ExecuteNonQueryAsync();
        }

        await using (var grant = adminConnection.CreateCommand())
        {
            grant.CommandText = "GRANT SELECT, INSERT ON properties TO ten21_app_test;";
            await grant.ExecuteNonQueryAsync();
        }

        // Full-stack audit finding (2026-08-27): charges is one of the 15 tables that went
        // live with an EF Core query filter but no RLS policy for several sprints (see
        // AddRowLevelSecurityForLedgerLeaseAndResidentTables' own migration comment). Granted
        // here so RawSql_CannotReadAnotherTenantsChargeRows below can prove the backfilled
        // policy actually took effect, the same way the original test above proves it for
        // properties -- catches this specific class of regression going forward.
        await using (var grant = adminConnection.CreateCommand())
        {
            grant.CommandText = "GRANT SELECT, INSERT ON charges TO ten21_app_test;";
            await grant.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private NpgsqlConnection CreateAppRoleConnection()
    {
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = "ten21_app_test",
            Password = AppRolePassword,
        };
        return new NpgsqlConnection(connectionStringBuilder.ConnectionString);
    }

    private static async Task SetActiveTenantAsync(NpgsqlConnection connection, Guid tenantId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.current_tenant_id', @tenantId, false);";
        command.Parameters.AddWithValue("tenantId", tenantId.ToString());
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task RawSql_CannotReadAnotherTenantsRows_EvenBypassingEfCoreFilter()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var connectionA = CreateAppRoleConnection();
        await connectionA.OpenAsync();
        await SetActiveTenantAsync(connectionA, tenantA);

        await using (var insert = connectionA.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO properties
                    ("Id", "TenantId", "Name", "PropertyType", "StreetAddress1", "City", "State", "PostalCode", "Country", "CreatedAt", "IsDeleted")
                VALUES
                    (@id, @tenantId, 'RLS Test Property', 'SingleFamily', '1 Tenant A Way', 'Salt Lake City', 'UT', '84000', 'USA', now(), false);
                """;
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("tenantId", tenantA);
            await insert.ExecuteNonQueryAsync();
        }

        // Positive control: tenant A can see its own row through this same role/connection --
        // proves the row genuinely exists and RLS isn't just blocking everything unconditionally.
        await using (var ownRead = connectionA.CreateCommand())
        {
            ownRead.CommandText = "SELECT count(*) FROM properties;";
            var ownCount = (long)(await ownRead.ExecuteScalarAsync())!;
            Assert.Equal(1, ownCount);
        }

        await using var connectionB = CreateAppRoleConnection();
        await connectionB.OpenAsync();
        await SetActiveTenantAsync(connectionB, tenantB);

        await using var crossTenantRead = connectionB.CreateCommand();
        crossTenantRead.CommandText = "SELECT * FROM properties;";
        await using var reader = await crossTenantRead.ExecuteReaderAsync();

        Assert.False(await reader.ReadAsync());
    }

    /// <summary>Full-stack audit finding (2026-08-27): representative regression test for the
    /// 15 tables backfilled with RLS by AddRowLevelSecurityForLedgerLeaseAndResidentTables --
    /// charges stands in for the rest (payment_transactions, leases, resident_profiles, etc.),
    /// all added via the exact same per-table Sql() loop in that migration. Not exhaustive
    /// over all 15 by design: this proves the migration's pattern actually takes effect against
    /// a real Postgres server, not that each of the 15 policies is independently miswired.</summary>
    [Fact]
    public async Task RawSql_CannotReadAnotherTenantsChargeRows_EvenBypassingEfCoreFilter()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var propertyId = Guid.NewGuid();

        await using var connectionA = CreateAppRoleConnection();
        await connectionA.OpenAsync();
        await SetActiveTenantAsync(connectionA, tenantA);

        await using (var insertProperty = connectionA.CreateCommand())
        {
            insertProperty.CommandText = """
                INSERT INTO properties
                    ("Id", "TenantId", "Name", "PropertyType", "StreetAddress1", "City", "State", "PostalCode", "Country", "CreatedAt", "IsDeleted")
                VALUES
                    (@id, @tenantId, 'RLS Test Property', 'SingleFamily', '1 Tenant A Way', 'Salt Lake City', 'UT', '84000', 'USA', now(), false);
                """;
            insertProperty.Parameters.AddWithValue("id", propertyId);
            insertProperty.Parameters.AddWithValue("tenantId", tenantA);
            await insertProperty.ExecuteNonQueryAsync();
        }

        await using (var insertCharge = connectionA.CreateCommand())
        {
            insertCharge.CommandText = """
                INSERT INTO charges
                    ("Id", "TenantId", "PropertyId", "Description", "Amount", "DueDate", "Category", "AllocationPriority", "IsStatutoryLocked", "Status", "CreatedAt", "IsDeleted")
                VALUES
                    (@id, @tenantId, @propertyId, 'RLS Test Charge', 100.00, now(), 'AddOn', 4, true, 'Active', now(), false);
                """;
            insertCharge.Parameters.AddWithValue("id", Guid.NewGuid());
            insertCharge.Parameters.AddWithValue("tenantId", tenantA);
            insertCharge.Parameters.AddWithValue("propertyId", propertyId);
            await insertCharge.ExecuteNonQueryAsync();
        }

        // Positive control, same reasoning as the properties test above.
        await using (var ownRead = connectionA.CreateCommand())
        {
            ownRead.CommandText = "SELECT count(*) FROM charges;";
            var ownCount = (long)(await ownRead.ExecuteScalarAsync())!;
            Assert.Equal(1, ownCount);
        }

        await using var connectionB = CreateAppRoleConnection();
        await connectionB.OpenAsync();
        await SetActiveTenantAsync(connectionB, tenantB);

        await using var crossTenantRead = connectionB.CreateCommand();
        crossTenantRead.CommandText = "SELECT * FROM charges;";
        await using var reader = await crossTenantRead.ExecuteReaderAsync();

        Assert.False(await reader.ReadAsync());
    }

    /// <summary>
    /// Audit Refinement Sprint: a process guardrail for the exact class of drift that
    /// produced the gap AddRowLevelSecurityForLedgerLeaseAndResidentTables fixed -- 15
    /// tables went 5 sprints deep with an EF Core query filter but no Postgres RLS policy,
    /// and nothing caught it. Mirrors the same reflection Ten21DbContext.OnModelCreating
    /// already uses to find every ITenantScopedEntity, then asserts a live `pg_policies` row
    /// exists for each one's mapped table -- so the NEXT missed table fails a test instead of
    /// silently shipping. tenant_memberships is the one deliberate, documented exception (see
    /// sql/rls-policies.sql's own long-form comment on the auth-bootstrap problem).
    /// </summary>
    [Fact]
    public async Task EveryTenantScopedEntityTable_HasARowLevelSecurityPolicy()
    {
        var deliberatelyExcluded = new HashSet<string> { "tenant_memberships" };

        var options = new DbContextOptionsBuilder<Ten21DbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using var db = new Ten21DbContext(options, new TenantContext());

        var tenantScopedTables = db.Model.GetEntityTypes()
            .Where(t => typeof(ITenantScopedEntity).IsAssignableFrom(t.ClrType))
            .Select(t => t.GetTableName())
            .Where(name => name is not null && !deliberatelyExcluded.Contains(name))
            .Cast<string>()
            .Distinct()
            .ToList();

        Assert.NotEmpty(tenantScopedTables);

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        var tablesMissingAPolicy = new List<string>();
        foreach (var table in tenantScopedTables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM pg_policies WHERE tablename = @table;";
            command.Parameters.AddWithValue("table", table);
            var policyCount = (long)(await command.ExecuteScalarAsync())!;
            if (policyCount == 0)
            {
                tablesMissingAPolicy.Add(table);
            }
        }

        Assert.True(
            tablesMissingAPolicy.Count == 0,
            $"These ITenantScopedEntity tables have no Postgres RLS policy: {string.Join(", ", tablesMissingAPolicy)}. " +
            "Add one in a migration (see AddRowLevelSecurityForLedgerLeaseAndResidentTables for the pattern), or add the " +
            "table to this test's deliberatelyExcluded set with a comment explaining why, matching tenant_memberships.");
    }
}
