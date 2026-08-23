namespace Ten21.Domain.Common;

/// <summary>
/// Permission prefixes that Tenant (non-owner renter) accounts are HARD-BLOCKED from,
/// regardless of what RolePermissions.Bundles says. SECURITY.docx §4.2's "Owner vs. Tenant
/// Isolation Principle" is treated as a defense-in-depth invariant, not just a starting
/// claims bundle -- even if a future change accidentally adds one of these permissions to
/// RolePermissions.Bundles[RoleNames.Tenant], TenantHardBlockAuthorizationHandler
/// (Infrastructure) still refuses it. Same belt-and-suspenders principle as the EF Core
/// filter + Postgres RLS pairing elsewhere in this codebase.
/// </summary>
public static class TenantRestrictedPermissionPrefixes
{
    public static readonly IReadOnlyList<string> Values =
    [
        "Permissions.Ledger.",
        "Permissions.Voting.",
    ];

    // SECURITY.docx also names "legal notices" and "delinquency reports" in this same
    // principle -- they don't have permission categories yet because no feature backs
    // them (Phase 2). Add prefixes here the moment those categories exist; don't let this
    // list silently fall behind Permissions.cs.
}
