using Microsoft.AspNetCore.Authorization;

namespace Ten21.Infrastructure.Authorization;

/// <summary>
/// One requirement type parameterized by permission string, rather than a distinct
/// requirement class per permission -- AuthorizationConfiguration registers one named
/// policy per Permissions.All entry, each wrapping this same requirement type with a
/// different Permission value.
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }
}
