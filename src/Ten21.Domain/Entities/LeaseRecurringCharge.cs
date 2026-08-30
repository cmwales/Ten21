using Ten21.Domain.Common;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-30: one recurring monthly sub-charge on a Lease (e.g. "Pet Rent", "Reserved Parking
/// #12"). Same shape/reasoning as EmergencyContact -- TenantId carried directly
/// (defense-in-depth) rather than derived solely through Lease, deliberately NOT ISoftDelete
/// since removing a line item from a lease's dues schedule is a genuine delete, not history
/// worth preserving on its own.
/// </summary>
public class LeaseRecurringCharge : ITenantScopedEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LeaseId { get; set; }

    public required string ChargeName { get; set; }
    public decimal Amount { get; set; }
    public string? AccountingCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
