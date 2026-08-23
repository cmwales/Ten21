namespace Ten21.Domain.Common;

/// <summary>
/// Marker + contract for any entity that belongs to exactly one tenant (HOA, PMC-managed
/// property, self-storage facility, etc.) and must therefore be structurally impossible to
/// read or write outside that tenant's boundary.
///
/// Implementing this interface is what makes an entity eligible for:
///   1. The reflection-based global query filter applied in Ten21DbContext.OnModelCreating.
///   2. Automatic TenantId population on insert (Ten21DbContext.SaveChangesAsync).
///   3. PostgreSQL Row-Level Security enforcement (see sql/rls-policies.sql), which is the
///      defense-in-depth backstop if a query filter is ever accidentally bypassed
///      (e.g. via IgnoreQueryFilters, raw SQL, or a missed EF Core release note).
///
/// Deliberately just one property. No base class is forced on entities -- interfaces
/// compose, base classes lock you in, and we don't yet know which entities will also need
/// ISoftDelete / IAuditable (US-07), so we're not pre-guessing that shape here.
/// </summary>
public interface ITenantScopedEntity
{
    Guid TenantId { get; set; }
}
