using Ten21.Domain.Common;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-31: a one-time, non-recurring financial charge or fine posted directly to a unit's
/// (or a specific resident's) ledger -- e.g. "Trash Violation Fine", "Playground Key Pass".
/// Nested under a Property, same BOLA/IDOR-safe convention as Lease/ResidentProfile.
/// Persisting one IS the "immediate balance impact" the acceptance criteria calls for -- this
/// sprint doesn't build a running-balance/payment-received engine (Phase 1, still pending),
/// it establishes the open line-item record a future balance calculation would sum.
/// Soft-deletable like Lease -- a posted charge/fine is a financial record, not something to
/// hard-erase if it turns out to need voiding.
/// </summary>
public class ManualCharge : ITenantScopedEntity, IAuditableEntity, ISoftDelete
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PropertyId { get; set; }

    /// <summary>Null means the charge is billed to the unit generally, not a specific
    /// resident -- e.g. a shared-area damage fine with no single resident at fault.</summary>
    public Guid? ResidentId { get; set; }

    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public DateOnly DueDate { get; set; }
    public string? AccountingCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
