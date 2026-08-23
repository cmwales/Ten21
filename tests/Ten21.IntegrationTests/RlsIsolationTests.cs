using Microsoft.EntityFrameworkCore;
using Npgsql;
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
                    ("Id", "TenantId", "StreetAddress", "City", "StateProvince", "PostalCode", "CreatedAt", "IsDeleted")
                VALUES
                    (@id, @tenantId, '1 Tenant A Way', 'Salt Lake City', 'UT', '84000', now(), false);
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
}
