using Microsoft.AspNetCore.Authorization;

namespace Ten21.Infrastructure.Authorization;

/// <summary>
/// Audit Refinement Sprint: a resource-based requirement (SECURITY.docx §3's "resource-based
/// ASP.NET Authorization Handlers inspecting entity ownership" -- previously undelivered;
/// BOLA/IDOR defense was 100% per-controller manual query-scoping convention, verified
/// correct everywhere but with no structural guardrail). One requirement type, reusable
/// across every ITenantScopedEntity resource, same "one requirement type parameterized"
/// shape as PermissionRequirement.
/// </summary>
public class SameTenantRequirement : IAuthorizationRequirement
{
}
