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
}
