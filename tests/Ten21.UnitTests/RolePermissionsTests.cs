using Ten21.Domain.Common;
using Xunit;

namespace Ten21.UnitTests;

public class RolePermissionsTests
{
    [Fact]
    public void EveryRoleName_HasABundleDefined()
    {
        foreach (var role in RoleNames.All)
        {
            Assert.True(
                RolePermissions.Bundles.ContainsKey(role),
                $"{role} has no RolePermissions bundle defined.");
        }
    }

    [Fact]
    public void TenantBundle_NeverIncludesARestrictedPermission()
    {
        // TenantHardBlockAuthorizationHandler exists as a backstop for exactly this
        // invariant -- this test verifies the PRIMARY layer (the bundle itself) also
        // honors it independently, so both halves of the defense-in-depth pairing are
        // each individually correct, not just the backstop covering for a wrong bundle.
        var tenantPermissions = RolePermissions.Bundles[RoleNames.Tenant];

        foreach (var permission in tenantPermissions)
        {
            var isRestricted = TenantRestrictedPermissionPrefixes.Values
                .Any(prefix => permission.StartsWith(prefix, StringComparison.Ordinal));

            Assert.False(isRestricted, $"Tenant bundle unexpectedly includes restricted permission {permission}");
        }
    }

    [Fact]
    public void SuperAdminBundle_IncludesEveryDefinedPermission()
    {
        Assert.Equal(Permissions.All.Count, RolePermissions.Bundles[RoleNames.SuperAdmin].Count);

        foreach (var permission in Permissions.All)
        {
            Assert.Contains(permission, RolePermissions.Bundles[RoleNames.SuperAdmin]);
        }
    }

    [Fact]
    public void PropertyManagerBundle_HasNoVotingPermission()
    {
        // SECURITY.docx §4.1: "Cannot cast HOA board votes" -- pinned as a test so this
        // specific, explicitly-stated restriction can't silently regress.
        var permissions = RolePermissions.Bundles[RoleNames.PropertyManager];

        Assert.DoesNotContain(permissions, p => p.StartsWith("Permissions.Voting.", StringComparison.Ordinal));
    }
}
