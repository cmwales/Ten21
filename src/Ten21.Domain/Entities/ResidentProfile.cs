using Ten21.Domain.Common;
using Ten21.Domain.Enums;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-23: one occupant (primary or secondary) of a specific Property row. Deliberately its
/// own entity rather than a repurposed TenantMembership -- TenantMembership already means
/// "this login has this role in this multi-tenancy partition" (no PropertyId, not
/// soft-deletable), which is a different relationship from "this person lives in this
/// unit" (has move-in/move-out history worth keeping). UserId links to the ApplicationUser
/// provisioned for this occupant (US-24) once/if they're given a login -- null until then,
/// and permanently null for an occupant who was never provisioned one.
///
/// Every occupant with an email is provisioned a login (US-24), not just the primary --
/// Primary vs Secondary here is a directory/display distinction only, not an access-level
/// one.
/// </summary>
public class ResidentProfile : ITenantScopedEntity, IAuditableEntity, ISoftDelete
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PropertyId { get; set; }
    public Guid? UserId { get; set; }

    public OccupantType OccupantType { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }

    /// <summary>US-23: only meaningful once NoticeGivenDate is set (the frontend
    /// conditionally reveals this field at that point) -- not enforced as a hard ordering
    /// constraint server-side, since a PM may reasonably capture both at once or edit one
    /// without touching the other.</summary>
    public string? ForwardingAddress { get; set; }

    public DateTimeOffset? NoticeGivenDate { get; set; }

    /// <summary>US-25: one half of the directory's dual-consent gate -- a resident only
    /// appears in the community directory when this AND the owning Property's
    /// AllowTenantDirectory are both true.</summary>
    public bool ShowInDirectory { get; set; }

    public ICollection<EmergencyContact> EmergencyContacts { get; set; } = new List<EmergencyContact>();

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public bool IsDeleted { get; set; }
}
