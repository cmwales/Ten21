using Ten21.Domain.Common;

namespace Ten21.Domain.Entities;

/// <summary>
/// One row per tracked change to an IAuditableEntity. Populated entirely by
/// AuditSaveChangesInterceptor -- nothing else in the codebase should insert into this
/// table directly.
/// </summary>
public class AuditLog : ITenantScopedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string EntityName { get; set; }
    public required string EntityId { get; set; }

    /// <summary>"Insert", "Update", or "Delete" -- "Delete" here means a SOFT delete
    /// (IsDeleted flipped to true), since ISoftDelete entities never generate a real
    /// DELETE statement in the first place.</summary>
    public required string Action { get; set; }

    public Guid? ChangedByUserId { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
    public string? OriginalValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
}
