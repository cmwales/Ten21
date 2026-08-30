using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;

namespace Ten21.Infrastructure.Persistence.Interceptors;

/// <summary>
/// US-07: Audit Logging & Soft Delete Interceptor.
///
/// Two jobs, both happening inside SavingChanges(Async) -- i.e. BEFORE EF Core generates
/// SQL for the current SaveChanges call -- so both the soft-delete conversion and the new
/// AuditLog rows are part of the exact same database transaction as the change they're
/// describing, not a separate follow-up write that could fail independently:
///
///   1. Soft-delete conversion: an EntityState.Deleted entry for an ISoftDelete entity
///      becomes EntityState.Modified with IsDeleted = true, so no real DELETE statement is
///      ever generated for that entity type -- UNLESS IHardDeleteOverride (US-22) has
///      explicitly marked that exact entity instance for a genuine hard delete, in which
///      case this interceptor leaves it alone.
///   2. Audit capture: every Added/Modified/Deleted entry for an IAuditableEntity gets a
///      corresponding AuditLog row with a JSON diff, added directly to the same
///      ChangeTracker mid-flight.
///
/// Only entities implementing IAuditableEntity are audited -- deliberately not every
/// entity in the DbContext, or ASP.NET Core Identity's own bookkeeping (AccessFailedCount
/// incrementing on every failed login, SecurityStamp rotating) would flood this table with
/// framework noise instead of meaningful business changes.
/// </summary>
public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenantContext;
    private readonly IHardDeleteOverride _hardDeleteOverride;

    public AuditSaveChangesInterceptor(ITenantContext tenantContext, IHardDeleteOverride hardDeleteOverride)
    {
        _tenantContext = tenantContext;
        _hardDeleteOverride = hardDeleteOverride;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ProcessChanges(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ProcessChanges(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ProcessChanges(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var auditEntries = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog)
            {
                continue; // never audit the audit log itself
            }

            // Soft-delete conversion happens regardless of whether this entity is also
            // audited -- ISoftDelete and IAuditableEntity are independent contracts.
            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDelete softDeletable)
            {
                // US-22: an explicit, per-instance opt-out -- leave it as a real delete.
                if (!_hardDeleteOverride.IsMarkedForHardDelete(entry.Entity))
                {
                    entry.State = EntityState.Modified;
                    softDeletable.IsDeleted = true;
                }
            }

            if (entry.Entity is not IAuditableEntity auditable)
            {
                continue;
            }

            // Must run before BuildAuditLog snapshots CurrentValues below, so the stamp
            // itself is captured in the audit row's own NewValuesJson too. Setting a
            // property here (mid-SavingChanges, before SQL generation) is picked up by EF
            // Core's change tracker like any other mutation -- same proven pattern as the
            // IsDeleted = true stamp above.
            if (entry.State == EntityState.Added)
            {
                auditable.CreatedByUserId = _tenantContext.UserId;
            }
            else if (entry.State == EntityState.Modified)
            {
                auditable.UpdatedAt = DateTimeOffset.UtcNow;
                auditable.UpdatedByUserId = _tenantContext.UserId;
            }

            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                auditEntries.Add(BuildAuditLog(entry));
            }
        }

        foreach (var auditEntry in auditEntries)
        {
            context.Set<AuditLog>().Add(auditEntry);
        }
    }

    private AuditLog BuildAuditLog(EntityEntry entry)
    {
        var action = entry.State switch
        {
            EntityState.Added => "Insert",
            EntityState.Modified => "Update", // includes the soft-delete conversion above
            EntityState.Deleted => "Delete",  // only reachable for non-ISoftDelete auditable entities
            _ => "Unknown",
        };

        // Ten21JsonOptions.Default (not JsonSerializerOptions.Default) so an enum-typed
        // property (Property.OccupancyStatus, Property.PropertyType, ...) diffs as its
        // string name here too, matching what the API itself sends/receives -- see
        // Ten21JsonOptions's own doc comment for the real bug this fixes.
        var originalValuesJson = entry.State == EntityState.Added
            ? null
            : JsonSerializer.Serialize(SnapshotValues(entry.OriginalValues), Ten21JsonOptions.Default);

        var newValuesJson = entry.State == EntityState.Deleted
            ? null
            : JsonSerializer.Serialize(SnapshotValues(entry.CurrentValues), Ten21JsonOptions.Default);

        var idProperty = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id");

        return new AuditLog
        {
            Id = Guid.NewGuid(),
            // Best-effort: background/seed operations with no resolved tenant context
            // (e.g. DevSeeder's very first Tenant insert, before it calls SetTenant) fall
            // back to Guid.Empty here, same fail-closed convention as everywhere else --
            // an audit row with an empty TenantId is a visible anomaly to investigate, not
            // a silent gap.
            TenantId = _tenantContext.TenantId ?? Guid.Empty,
            EntityName = entry.Entity.GetType().Name,
            EntityId = idProperty?.CurrentValue?.ToString() ?? "unknown",
            Action = action,
            ChangedByUserId = _tenantContext.UserId,
            ChangedAtUtc = DateTimeOffset.UtcNow,
            OriginalValuesJson = originalValuesJson,
            NewValuesJson = newValuesJson,
        };
    }

    private static Dictionary<string, object?> SnapshotValues(PropertyValues values) =>
        values.Properties.ToDictionary(p => p.Name, p => values[p]);
}
