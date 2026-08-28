using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Ten21.Domain.Common;
using Ten21.Domain.Exceptions;

namespace Ten21.Infrastructure.Persistence;

/// <summary>
/// Audit Refinement Sprint: extracts two query patterns an audit found independently
/// reimplemented, byte-for-byte identical, across 5+ controllers -- "does this property exist
/// in the active tenant" (ChargesController, PaymentsController, DepositsController,
/// CreditsController, RefundsController, ResidentsController) and "what's this resident's
/// display name" (PaymentsController, DepositsController, RefundsController). Extension
/// methods on Ten21DbContext rather than a separate injected service -- every call site
/// already has the DbContext in hand, and this is pure query composition, not a service with
/// its own state/lifetime concerns.
/// </summary>
public static class Ten21DbContextQueryExtensions
{
    public static async Task EnsurePropertyExistsAsync(
        this Ten21DbContext dbContext, Guid propertyId, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Properties.AsNoTracking().AnyAsync(p => p.Id == propertyId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException($"Property '{propertyId}' was not found.");
        }
    }

    public static async Task<string> GetResidentNameAsync(
        this Ten21DbContext dbContext, Guid residentProfileId, CancellationToken cancellationToken)
    {
        var resident = await dbContext.ResidentProfiles.AsNoTracking()
            .Where(r => r.Id == residentProfileId)
            .Select(r => new { r.FirstName, r.LastName })
            .FirstOrDefaultAsync(cancellationToken);
        return resident is null ? "(unknown resident)" : $"{resident.FirstName} {resident.LastName}";
    }

    /// <summary>Bulk variant for the statement/rollup-style callers that need many resident
    /// names at once (avoids issuing GetResidentNameAsync in a per-row loop).</summary>
    public static async Task<Dictionary<Guid, string>> GetResidentNamesAsync(
        this Ten21DbContext dbContext, IEnumerable<Guid> residentProfileIds, CancellationToken cancellationToken)
    {
        var ids = residentProfileIds.Distinct().ToList();
        return await dbContext.ResidentProfiles.AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => $"{r.FirstName} {r.LastName}", cancellationToken);
    }

    private static readonly MethodInfo ExistsInSetMethod = typeof(Ten21DbContextQueryExtensions)
        .GetMethod(nameof(ExistsInSetAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Audit Refinement Sprint: DocumentsController.PresignUpload's EntityId ownership check.
    /// Category is a free-text S3-key path segment (`{TenantId}/{Category}/{EntityId}/...`),
    /// not a typed enum backed by any built consumer yet (the photo-vault/maintenance-photo
    /// features it exists for are still unbuilt, per MVP_features.md) -- so there's no fixed
    /// "Category X always means table Y" mapping to validate against without inventing scope
    /// this codebase hasn't built. Instead, this checks EntityId against EVERY
    /// ITenantScopedEntity table generically, the same reflection-over-registration pattern
    /// Ten21DbContext.OnModelCreating already uses for the query filter itself: since every
    /// DbSet is already scoped to the active tenant automatically, a match on ANY table proves
    /// the id belongs to the caller's own tenant; no match means either the id doesn't exist
    /// at all or it belongs to a different tenant -- both cases the caller has no business
    /// attaching an upload to.
    /// </summary>
    public static async Task<bool> AnyTenantScopedRecordExistsAsync(
        this Ten21DbContext dbContext, Guid id, CancellationToken cancellationToken)
    {
        foreach (var entityType in dbContext.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (!typeof(ITenantScopedEntity).IsAssignableFrom(clrType) || clrType.GetProperty("Id") is null)
            {
                continue;
            }

            var exists = await (Task<bool>)ExistsInSetMethod.MakeGenericMethod(clrType)
                .Invoke(null, [dbContext, id, cancellationToken])!;
            if (exists)
            {
                return true;
            }
        }

        return false;
    }

    private static Task<bool> ExistsInSetAsync<TEntity>(Ten21DbContext dbContext, Guid id, CancellationToken cancellationToken)
        where TEntity : class =>
        dbContext.Set<TEntity>().AnyAsync(e => EF.Property<Guid>(e, "Id") == id, cancellationToken);
}
