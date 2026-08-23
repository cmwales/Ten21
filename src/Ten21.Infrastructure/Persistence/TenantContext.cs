using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Persistence;

/// <summary>
/// Per-request implementation of ITenantContext. Must be registered as Scoped in DI so a
/// fresh instance exists per HTTP request (and per test/background-job scope).
/// </summary>
public class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public Guid? OrganizationId { get; private set; }
    public Guid? UserId { get; private set; }
    public bool IsResolved => TenantId.HasValue;

    public void SetTenant(Guid tenantId, Guid? organizationId = null)
    {
        if (IsResolved)
        {
            throw new InvalidOperationException(
                "Tenant context is already set for this scope and cannot be changed mid-request. " +
                "Switching properties requires a new scoped JWT via " +
                "POST /api/organization/switch-context, not a mutation of the current context.");
        }

        TenantId = tenantId;
        OrganizationId = organizationId;
    }

    public void SetUser(Guid userId)
    {
        if (UserId.HasValue)
        {
            throw new InvalidOperationException("User context is already set for this scope.");
        }

        UserId = userId;
    }
}
