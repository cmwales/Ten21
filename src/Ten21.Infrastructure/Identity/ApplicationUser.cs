using Microsoft.AspNetCore.Identity;

namespace Ten21.Infrastructure.Identity;

/// <summary>
/// The global identity record -- one row per person, full stop, regardless of how many
/// tenants they operate in. Lives in Infrastructure (not Domain) because IdentityUser&lt;T&gt;
/// is fundamentally a persistence-framework type (SecurityStamp, ConcurrencyStamp,
/// NormalizedEmail are ASP.NET Core Identity plumbing, not domain concepts).
///
/// No TenantId here -- see TenantMembership (Domain) for where tenant/role scoping
/// actually lives. This is the resolution to the US-02 design conflict: making this
/// tenant-scoped would force duplicate accounts per property and break both SSO and
/// stateless context switching (US-04).
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Freeform mailing address captured at registration (US-14). No structured
    /// address schema exists yet in DATA_MODEL.md -- kept as a single field rather than
    /// inventing one speculatively.</summary>
    public string? Address { get; set; }

    /// <summary>Set exactly once, at the moment of registration -- never inferred or
    /// defaulted. Null for any account that predates US-14 (e.g. seeded directly).</summary>
    public DateTimeOffset? AgreedToTermsAt { get; set; }

    /// <summary>
    /// US-24: true for an account provisioned with an auto-generated temporary password
    /// (e.g. a resident invited via ResidentsController), false for every self-registered
    /// (US-14) or Google (US-15) account, which set their own password/have none.
    /// AuthController.Login checks this BEFORE resolving tenant membership or checking 2FA
    /// -- a true value short-circuits straight into a password-change challenge (mirroring
    /// US-17's 2FA challenge-token pattern) instead of issuing a real session.
    /// </summary>
    public bool MustChangePassword { get; set; }
}
