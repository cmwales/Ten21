using Ten21.Domain.Common;

namespace Ten21.Domain.Entities;

/// <summary>
/// The bridge between a global ApplicationUser identity and a role scoped to one tenant.
/// This is where "which properties can this person act in, and as what role" actually
/// lives -- ApplicationUser itself carries no tenant information at all.
///
/// One person can have multiple TenantMembership rows (PMC staff across a portfolio, an
/// owner who's also a board member elsewhere) without needing multiple logins -- that's
/// the entire reason this exists as a separate entity instead of a TenantId column on
/// ApplicationUser (see the Phase 0 US-02 design discussion this resolves).
///
/// Deliberately holds scalar Guid FKs (UserId, RoleId) rather than navigation properties to
/// ApplicationUser/ApplicationRole -- those types live in Infrastructure (they're
/// ASP.NET Core Identity framework types), and Domain must not depend on Infrastructure.
/// The actual EF relationships are wired up via Fluent API in Infrastructure's
/// TenantMembershipConfiguration, using HasForeignKey against these scalar properties.
///
/// IAuditableEntity (US-07): who has what role in which tenant is exactly the kind of
/// change a Compliance Officer needs a historical trail for -- grants, revocations, and
/// role changes all get an AuditLog row automatically. Deliberately NOT ISoftDelete,
/// though: offboarding/revocation semantics (does removing access preserve history, does
/// it need its own approval flow) aren't designed yet -- that's Phase 2 territory, not
/// something to bolt on speculatively here.
/// </summary>
public class TenantMembership : ITenantScopedEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }

    /// <summary>
    /// True for exactly one TenantMembership per UserId -- the tenant context selected by
    /// default at login, before any explicit context-switch. Enforced by application logic
    /// (AuthController), not a DB constraint, since "exactly one" across an unbounded set
    /// of rows isn't expressible as a simple unique index.
    /// </summary>
    public bool IsPrimary { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
