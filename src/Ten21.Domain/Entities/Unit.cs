using Ten21.Domain.Common;
using Ten21.Domain.Enums;

namespace Ten21.Domain.Entities;

/// <summary>
/// A single leasable unit/suite/lot belonging to exactly one Property (US-19). Carries its
/// own TenantId rather than deriving isolation solely through Property -- same
/// defense-in-depth convention as every other tenant-scoped entity (US-01), so a bug in a
/// join/include doesn't become a cross-tenant leak.
/// </summary>
public class Unit : ITenantScopedEntity, IAuditableEntity, ISoftDelete
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PropertyId { get; set; }
    public required string UnitIdentifier { get; set; }
    public decimal? TargetRent { get; set; }
    public OccupancyStatus OccupancyStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDeleted { get; set; }

    public Property? Property { get; set; }
}
