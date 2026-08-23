using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Database-level backstop for tenant isolation (defense-in-depth alongside the EF Core
/// query filter in Ten21DbContext).
///
/// On every logical connection open, stamps the active tenant into a Postgres session
/// variable (app.current_tenant_id) that RLS policies key off of -- see
/// sql/rls-policies.sql. This means even a raw/unfiltered SQL query, or a new entity that
/// forgot to implement ITenantScopedEntity, still cannot read or write another tenant's
/// rows: the database refuses at the row level regardless of what the application layer did.
///
/// Npgsql fires ConnectionOpened on every logical Open() call -- even when the underlying
/// physical socket is reused from the pool -- so this always runs fresh per request. There
/// is no risk of a stale tenant id leaking across pooled connections between requests.
///
/// When no tenant is resolved (anonymous endpoints), the session variable is set to
/// Guid.Empty, matching the EF Core filter's fail-closed default -- RLS policies compare
/// against a real (never-assigned) UUID rather than needing to special-case NULL.
/// </summary>
public class TenantSessionInterceptor : DbConnectionInterceptor
{
    private const string SetSessionTenantSql = "SELECT set_config('app.current_tenant_id', @tenantId, false);";

    private readonly ITenantContext _tenantContext;

    public TenantSessionInterceptor(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateTenantCommand(connection);
        command.ExecuteNonQuery();
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        var command = CreateTenantCommand(connection);
        await using (command.ConfigureAwait(false))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private DbCommand CreateTenantCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = SetSessionTenantSql;
        command.Parameters.Add(new NpgsqlParameter(
            "tenantId",
            _tenantContext.TenantId?.ToString() ?? Guid.Empty.ToString()));
        return command;
    }
}
