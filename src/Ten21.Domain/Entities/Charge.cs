using Ten21.Domain.Common;
using Ten21.Domain.Enums;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-31/US-34/US-35 (renamed from ManualCharge, Sprint 7): any billable line item posted to
/// a unit's ledger -- a one-time fine ("Trash Violation Fine"), a pro-rated move-in charge, or
/// a period's base rent/add-on, all the same shape. Renamed from ManualCharge because it now
/// covers rent too, not just ad-hoc fees -- there's no automated recurring-billing engine yet
/// (Phase 2), so a PM manually posts every charge each period, including rent, categorized
/// via Category. Nested under a Property, same BOLA/IDOR-safe convention as
/// Lease/ResidentProfile. Never tied to a resident -- see the class's prior history for why
/// (tester feedback: charges are billed to the unit, not an individual occupant).
///
/// PaymentStatus (Unpaid/Partial/Paid) is deliberately NOT stored here -- it's computed at
/// read time from Sum(PaymentAllocation.AllocatedAmount) for this charge vs Amount, since a
/// charge can now be paid across multiple partial payments on different dates. That's also
/// why the PaidDate field from the prior fix was removed: PaymentTransaction.PaymentDate
/// supersedes it with a per-payment date instead of one scalar on the charge.
/// </summary>
public class Charge : ITenantScopedEntity, IAuditableEntity, ISoftDelete
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PropertyId { get; set; }

    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public DateOnly DueDate { get; set; }
    public string? AccountingCode { get; set; }

    /// <summary>Refinement Sprint: free-text notes about the charge (e.g. context a PM wants
    /// on record beyond the short Description label) -- distinct from Description, which is
    /// the charge's required title/label shown throughout the UI.</summary>
    public string? Notes { get; set; }

    /// <summary>US-44 (Sprint 9): set only for a charge BillingCycleService generated from a
    /// LeaseRecurringCharge template; null for manually-posted/one-off charges (fines,
    /// pro-rated move-in charges). Paired with DueDate, this is the idempotency key
    /// generation checks before posting -- a retried cycle never double-bills the same
    /// template for the same period.</summary>
    public Guid? SourceRecurringChargeId { get; set; }

    public ChargeCategory Category { get; set; }

    /// <summary>1 (highest) - 10 (lowest). Defaults from Category via
    /// DefaultAllocationPriorityFor -- stored rather than always re-derived so it's directly
    /// queryable/sortable, and so a future statutory-override UI (gated by
    /// IsStatutoryLocked) has somewhere to write a non-default value without a schema change.
    /// No override UI exists yet -- every charge created this sprint gets the category
    /// default.</summary>
    public int AllocationPriority { get; set; }

    /// <summary>Default true: this charge's AllocationPriority follows the statutory
    /// category order and cannot be manually overridden. No override UI built yet -- the
    /// flag exists so the schema doesn't need to change when one is.</summary>
    public bool IsStatutoryLocked { get; set; } = true;

    /// <summary>Active vs Voided -- see ChargeLifecycleStatus's own doc comment. Distinct
    /// from the computed PaymentStatus badge.</summary>
    public ChargeLifecycleStatus Status { get; set; } = ChargeLifecycleStatus.Active;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>US-34's statutory waterfall order: Late Fees/Interest, then Legal, then Base
    /// Rent, then Add-Ons. SpecialAssessment isn't named in the given order -- placed after
    /// AddOn as a sensible default (an HOA-style special assessment is unusual but not
    /// time-critical the way a late fee is).</summary>
    public static int DefaultAllocationPriorityFor(ChargeCategory category) => category switch
    {
        ChargeCategory.LateFee => 1,
        ChargeCategory.Legal => 2,
        ChargeCategory.BaseRent => 3,
        ChargeCategory.AddOn => 4,
        ChargeCategory.SpecialAssessment => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };
}
