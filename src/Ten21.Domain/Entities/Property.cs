using Ten21.Domain.Common;
using Ten21.Domain.Enums;

namespace Ten21.Domain.Entities;

/// <summary>
/// A managed property (single HOA lot, rental unit building, self-storage facility, etc.)
/// belonging to exactly one tenant.
///
/// Sprint 3 (US-19) is the "expand this significantly" promised by this class's original
/// US-01/US-07 proof-of-concept comment: real classification, a broken-out address, and a
/// DefaultTargetRent that seeds new child Units at creation time (a one-time default, not a
/// live formula -- see UnitListEditor's frontend comment).
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
    public decimal? DefaultTargetRent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDeleted { get; set; }

    public ICollection<Unit> Units { get; set; } = new List<Unit>();
}
