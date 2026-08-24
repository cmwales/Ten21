using Ten21.Domain.Common;
using Ten21.Domain.Enums;

namespace Ten21.Domain.Entities;

/// <summary>
/// A single leasable space belonging to exactly one tenant -- a standalone single-family
/// house, OR one suite/unit within a larger building. There is deliberately no separate
/// parent/child Property-and-Unit relationship: Suite A and Suite B of the same building are
/// two independent Property rows that happen to share a street address, distinguished by
/// UnitIdentifier. This flat shape replaced an earlier Property-with-child-Units design
/// (US-19-22) after tester feedback: "Each suite in a building needs to be a new property.
/// They need to be setup independently" -- see User_Stories_Sprint_3.md's "Flatten
/// Property/Unit" addendum for the full history and the removed Unit entity's old shape.
/// </summary>
public class Property : ITenantScopedEntity, IAuditableEntity, ISoftDelete
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public PropertyType PropertyType { get; set; }
    public required string StreetAddress1 { get; set; }
    public string? StreetAddress2 { get; set; }
    public required string City { get; set; }
    public required string State { get; set; }
    public required string PostalCode { get; set; }
    public required string Country { get; set; }

    /// <summary>Suite/apartment/unit number -- null for a standalone single-family property,
    /// populated (e.g. "Suite A") for one leasable space within a shared-address
    /// building.</summary>
    public string? UnitIdentifier { get; set; }

    public decimal? TargetRent { get; set; }
    public OccupancyStatus OccupancyStatus { get; set; }

    /// <summary>US-25: one half of the community directory's dual-consent gate -- a
    /// resident of THIS property only appears in another resident's directory query when
    /// this AND that ResidentProfile's own ShowInDirectory are both true. Defaults false --
    /// a PM must opt a property into the directory explicitly, per-property, not have it on
    /// by default.</summary>
    public bool AllowTenantDirectory { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
