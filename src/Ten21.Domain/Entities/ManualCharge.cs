using Ten21.Domain.Common;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-31: a one-time, non-recurring financial charge or fine posted directly to a unit's
/// ledger -- e.g. "Trash Violation Fine", "Playground Key Pass". Nested under a Property,
/// same BOLA/IDOR-safe convention as Lease/ResidentProfile. Persisting one IS the "immediate
/// balance impact" the acceptance criteria calls for -- this sprint doesn't build a
/// running-balance engine (Phase 1, still pending), it establishes the open line-item record
/// a future balance calculation would sum. Soft-deletable like Lease -- a posted charge/fine
/// is a financial record, not something to hard-erase if it turns out to need voiding.
///
/// Post-Sprint-6 fix, tester feedback: originally had an optional ResidentId ("bill to a
/// specific resident or the unit generally"). Removed entirely -- rent/charges aren't billed
/// to an individual occupant, they're billed to the unit, so a per-resident "bill to" never
/// matched how collection actually works.
/// </summary>
public class ManualCharge : ITenantScopedEntity, IAuditableEntity, ISoftDelete
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PropertyId { get; set; }

    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public DateOnly DueDate { get; set; }
    public string? AccountingCode { get; set; }

    /// <summary>The date payment was actually RECEIVED (check/cash in hand), not the date
    /// someone got around to entering it here -- those can legitimately differ by days.
    /// Null means still unpaid/outstanding.</summary>
    public DateOnly? PaidDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
