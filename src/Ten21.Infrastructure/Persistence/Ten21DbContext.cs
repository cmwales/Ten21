using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Infrastructure.Identity;

namespace Ten21.Infrastructure.Persistence;

/// <summary>
/// US-01 Multi-Tenant Data Isolation Engine + US-02 Identity/Refresh Token persistence +
/// US-07 Audit Logging & Soft Delete (query-filter half; the write-time half -- capturing
/// diffs and converting deletes to soft-deletes -- lives in AuditSaveChangesInterceptor).
///
/// Two independent isolation mechanisms, both driven by the same ITenantContext:
///   1. READS: a reflection-based global query filter is applied to every entity that
///      implements ITenantScopedEntity and/or ISoftDelete, so new entities get isolation
///      "for free" the moment they implement the interface -- no per-entity
///      OnModelCreating code required.
///   2. WRITES: SaveChanges/SaveChangesAsync auto-stamp TenantId on inserts and refuse
///      updates that don't belong to the active tenant.
///
/// A third, independent mechanism (Postgres RLS, via TenantSessionInterceptor +
/// sql/rls-policies.sql) backstops the tenant half of this at the database session level,
/// so a bug in this class specifically does not equal a cross-tenant data leak.
///
/// Extends IdentityDbContext&lt;ApplicationUser, ApplicationRole, Guid&gt; rather than plain
/// DbContext as of US-02 -- this is what AddEntityFrameworkStores&lt;Ten21DbContext&gt;() expects,
/// and it's why base.OnModelCreating() is called FIRST below: it builds the AspNetUsers /
/// AspNetRoles / AspNetUserRoles / etc. schema, which our own configuration and reflection
/// loop then layer on top of.
/// </summary>
public class Ten21DbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    private readonly ITenantContext _tenantContext;

    public Ten21DbContext(DbContextOptions<Ten21DbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<DocumentAttachment> DocumentAttachments => Set<DocumentAttachment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ResidentProfile> ResidentProfiles => Set<ResidentProfile>();
    public DbSet<EmergencyContact> EmergencyContacts => Set<EmergencyContact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Builds the Identity schema (AspNetUsers, AspNetRoles, AspNetUserRoles, etc.)
        // first, so our own table configurations and the tenant-filter reflection loop
        // below see the complete model, including the entity types Identity itself adds.
        base.OnModelCreating(modelBuilder);

        // Renaming the rest of Identity's default AspNet* tables to snake_case for
        // consistency with tenants/organizations/properties/tenant_memberships.
        // ApplicationUser gets its own IEntityTypeConfiguration (below, via
        // ApplyConfigurationsFromAssembly) since it also needs the unique-email index;
        // these are pure renames with nothing else to configure, so inline is simpler than
        // four near-empty configuration classes.
        modelBuilder.Entity<ApplicationRole>(b => b.ToTable("roles"));
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>(b => b.ToTable("user_roles"));
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>(b => b.ToTable("user_claims"));
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>(b => b.ToTable("user_logins"));
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>(b => b.ToTable("user_tokens"));
        modelBuilder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>(b => b.ToTable("role_claims"));

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(Ten21DbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            var isTenantScoped = typeof(ITenantScopedEntity).IsAssignableFrom(clrType);
            var isSoftDelete = typeof(ISoftDelete).IsAssignableFrom(clrType);

            if (!isTenantScoped && !isSoftDelete)
                continue;

            // Three possible filter shapes depending on which interface(s) this entity
            // implements -- Property (US-07's demo entity) is both, so the filters need to
            // AND together rather than the second one silently overwriting the first.
            var filterMethodName = (isTenantScoped, isSoftDelete) switch
            {
                (true, true) => nameof(BuildTenantAndSoftDeleteFilter),
                (true, false) => nameof(BuildTenantFilter),
                (false, true) => nameof(BuildSoftDeleteFilter),
                _ => throw new InvalidOperationException("Unreachable -- guarded by the continue above."),
            };

            var filterMethod = typeof(Ten21DbContext)
                .GetMethod(filterMethodName, BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(clrType);

            var filter = (LambdaExpression)filterMethod.Invoke(this, null)!;
            entityType.SetQueryFilter(filter);

            if (isTenantScoped)
            {
                // Every tenant-scoped entity is queried by TenantId on every single
                // request -- indexing it isn't optional/premature, it's table stakes.
                modelBuilder.Entity(clrType).HasIndex(nameof(ITenantScopedEntity.TenantId));
            }
        }
    }

    /// <summary>
    /// Fail-closed by design. If TenantId is unresolved (null), the filter compares against
    /// Guid.Empty rather than being skipped -- an unresolved tenant context returns zero
    /// rows for every tenant-scoped entity, never "everything."
    ///
    /// Uses GetValueOrDefault() rather than "?? Guid.Empty": the null-coalescing form
    /// mistranslates under EF Core 9.0.1 (the version Npgsql.EntityFrameworkCore.PostgreSQL
    /// 9.0.4 forces) against the SQLite provider used in tests -- it throws
    /// InvalidOperationException ("Nullable object must have a value") instead of matching
    /// zero rows when TenantId is null. GetValueOrDefault() is semantically identical
    /// (Guid.Empty when null) and translates correctly on both providers.
    /// </summary>
    private LambdaExpression BuildTenantFilter<TEntity>() where TEntity : class, ITenantScopedEntity
    {
        Expression<Func<TEntity, bool>> filter = e => e.TenantId == _tenantContext.TenantId.GetValueOrDefault();
        return filter;
    }

    /// <summary>Excludes soft-deleted rows from every normal query. Use IgnoreQueryFilters()
    /// at the (rare, deliberate) call site that legitimately needs to see deleted rows.</summary>
    private LambdaExpression BuildSoftDeleteFilter<TEntity>() where TEntity : class, ISoftDelete
    {
        Expression<Func<TEntity, bool>> filter = e => !e.IsDeleted;
        return filter;
    }

    /// <summary>Both conditions ANDed together for entities (like Property) that are both
    /// tenant-scoped and soft-deletable -- neither filter silently overwrites the other.
    /// Uses GetValueOrDefault(), see the comment on BuildTenantFilter above.</summary>
    private LambdaExpression BuildTenantAndSoftDeleteFilter<TEntity>()
        where TEntity : class, ITenantScopedEntity, ISoftDelete
    {
        Expression<Func<TEntity, bool>> filter =
            e => e.TenantId == _tenantContext.TenantId.GetValueOrDefault() && !e.IsDeleted;
        return filter;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantStamping();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTenantStamping();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Auto-populates TenantId on inserts (US-01 acceptance criterion) and, as a
    /// defense-in-depth addition beyond the acceptance criteria, hard-blocks any update to
    /// a row that doesn't belong to the active tenant -- this catches the case where a row
    /// was loaded via IgnoreQueryFilters() (e.g. SuperAdmin tooling) and then mistakenly
    /// saved without re-checking tenant ownership.
    /// </summary>
    private void ApplyTenantStamping()
    {
        var activeTenantId = _tenantContext.TenantId;

        foreach (var entry in ChangeTracker.Entries<ITenantScopedEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (activeTenantId is null)
                    {
                        throw new InvalidOperationException(
                            $"Cannot insert {entry.Entity.GetType().Name}: no active tenant " +
                            "context is set for this scope.");
                    }
                    entry.Entity.TenantId = activeTenantId.Value;
                    break;

                case EntityState.Modified:
                    if (activeTenantId is null || entry.Entity.TenantId != activeTenantId.Value)
                    {
                        throw new InvalidOperationException(
                            $"Cannot modify {entry.Entity.GetType().Name}: entity does not " +
                            "belong to the active tenant context.");
                    }
                    break;
            }
        }
    }
}
