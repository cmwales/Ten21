using System.Runtime.CompilerServices;
using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Persistence;

/// <summary>US-26: reference-equality map, not value-equality -- same reasoning as
/// HardDeleteOverride, only the exact tracked instance a controller marked gets the
/// override.</summary>
public class TenantStampOverride : ITenantStampOverride
{
    private readonly Dictionary<object, Guid> _overrides = new(ReferenceEqualityComparer.Instance);

    public void MarkTenantId(object entity, Guid tenantId) => _overrides[entity] = tenantId;

    public Guid? GetOverride(object entity) => _overrides.TryGetValue(entity, out var tenantId) ? tenantId : null;
}
