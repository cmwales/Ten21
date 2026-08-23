using Ten21.Domain.Common;

namespace Ten21.Domain.Entities;

/// <summary>
/// A managed property (single HOA lot, rental unit building, self-storage facility, etc.)
/// belonging to exactly one tenant.
///
/// This is intentionally the *first* real tenant-scoped entity in the codebase -- its job
/// is to prove US-01 (isolation engine) and now also US-07 (audit logging + soft delete)
/// end-to-end. Full property/unit modeling is Phase 3 (DATA_MODEL) work and will expand
/// this significantly.
/// </summary>
public class Property : ITenantScopedEntity, IAuditableEntity, ISoftDelete
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string StreetAddress { get; set; }
    public required string City { get; set; }
    public required string StateProvince { get; set; }
    public required string PostalCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
