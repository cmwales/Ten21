using Microsoft.AspNetCore.Identity;

namespace Ten21.Infrastructure.Identity;

/// <summary>
/// Identity role type backing the 9-tier taxonomy (see Ten21.Domain.Common.RoleNames for
/// the actual name constants). Deliberately a thin subclass with no extra fields yet --
/// US-03's domain-neutral role filtering (activating/deactivating roles per property type)
/// is expected to add fields here, not before it's actually needed.
/// </summary>
public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }

    public ApplicationRole(string roleName) : base(roleName) { }
}
