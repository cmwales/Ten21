using Ten21.Domain.Common;
using Ten21.Domain.Enums;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-44 (Sprint 9): a generic recurring charge template -- base rent and every add-on
/// (Pet Rent, Parking, Storage) are now the same shape, distinguished only by Category.
/// Previously (Sprint 6/US-30) this only covered add-ons, with base rent living on
/// Lease.MonthlyBaseRent/DueDayOfMonth directly; those two Lease fields were removed and
/// migrated into a generated LeaseRecurringCharge(Category = BaseRent) row per lease --
/// see the AddRecurringChargeTemplateFields migration. Same shape/reasoning as
/// EmergencyContact -- TenantId carried directly (defense-in-depth) rather than derived
/// solely through Lease, deliberately NOT ISoftDelete since removing a line item from a
/// lease's dues schedule is a genuine delete, not history worth preserving on its own.
///
/// DueDayOfMonth is stored honestly (1-31, not capped to 28 the way the old
/// Lease.DueDayOfMonth was) -- BillingCycleService clamps at execution time via
/// min(DueDayOfMonth, DaysInMonth(year, month)), so "the 31st" resolves to Feb 28/29
/// without ever mutating this stored value. See BillingCycleService.IsDueOn.
/// </summary>
public class LeaseRecurringCharge : ITenantScopedEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid LeaseId { get; set; }

    public required string ChargeName { get; set; }
    public ChargeCategory Category { get; set; }
    public decimal Amount { get; set; }
    public string? AccountingCode { get; set; }
    public string? Description { get; set; }

    public RecurrencePattern RecurrencePattern { get; set; }
    public int RecurrenceInterval { get; set; } = 1;

    /// <summary>1-31. Required for Monthly/SemiMonthly (primary due day); unused for
    /// Weekly/BiWeekly (TargetDayOfWeek instead) and Daily/Custom (step from
    /// EffectiveStartDate instead). Validated against RecurrencePattern in
    /// LeaseService/BillingTemplateValidation, not via a DB constraint, since the required
    /// field genuinely depends on which pattern is selected.</summary>
    public int? DueDayOfMonth { get; set; }

    public DayOfWeek? TargetDayOfWeek { get; set; }

    /// <summary>SemiMonthly's second due day (e.g. DueDayOfMonth=1, SecondaryDueDay=15).</summary>
    public int? SecondaryDueDay { get; set; }

    public EndStrategy EndStrategy { get; set; }
    public DateOnly EffectiveStartDate { get; set; }

    /// <summary>Only meaningful when EndStrategy = FixedDate. LeaseAligned reads the
    /// parent Lease's EndDate dynamically instead (a renewal shouldn't require touching
    /// every template); Indefinite ignores this entirely.</summary>
    public DateOnly? EffectiveEndDate { get; set; }

    public ProrationStrategy ProrationStrategy { get; set; }

    /// <summary>Freezes generation without deleting the template/its history.</summary>
    public bool IsPaused { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
