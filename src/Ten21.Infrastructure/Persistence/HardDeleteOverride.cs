using System.Runtime.CompilerServices;
using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Persistence;

/// <summary>US-22: reference-equality set, not value-equality -- two different Property
/// instances with the same Id must never be conflated here, only the exact tracked instance
/// a controller marked is exempted from soft-delete conversion.</summary>
public class HardDeleteOverride : IHardDeleteOverride
{
    private readonly HashSet<object> _entities = new(ReferenceEqualityComparer.Instance);

    public void MarkForHardDelete(object entity) => _entities.Add(entity);

    public bool IsMarkedForHardDelete(object entity) => _entities.Contains(entity);
}
