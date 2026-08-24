using Ten21.Domain.Common;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-23: one emergency contact for a ResidentProfile -- one-to-many (a resident can list
/// more than one). TenantId carried directly (defense-in-depth, same convention as every
/// other tenant-scoped entity) rather than derived solely through ResidentProfile.
/// Deliberately NOT ISoftDelete, same reasoning as TenantMembership: this is contact
/// metadata with no independent audit/compliance need of its own -- removing one from a
/// resident's list is a genuine delete, not a state to preserve history for.
/// </summary>
public class EmergencyContact : ITenantScopedEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ResidentProfileId { get; set; }

    public required string Name { get; set; }
    public required string PhoneNumber { get; set; }
    public string? Relationship { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
