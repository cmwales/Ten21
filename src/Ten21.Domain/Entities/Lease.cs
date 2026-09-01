using Ten21.Domain.Common;
using Ten21.Domain.Enums;

namespace Ten21.Domain.Entities;

/// <summary>
/// US-30: binds one ResidentProfile to one Property (a "unit" in the flattened model -- see
/// Property's own class comment) with contract dates and optional recurring charges.
/// Nested under a Property the same way ResidentProfile/EmergencyContact are, per
/// CLAUDE.md's BOLA/IDOR resource-based-auth mandate: LeasesController re-checks
/// PropertyId == the route's propertyId on every action rather than trusting a bare {id}
/// lookup.
///
/// US-44 (Sprint 9): MonthlyBaseRent/DueDayOfMonth were removed from here -- base rent is
/// now just another RecurringCharges row (Category = BaseRent), unified with every add-on
/// under the same recurring-charge-template engine (see LeaseRecurringCharge's own class
/// comment). TotalMonthlyDues (Sum(RecurringCharges.Amount)) is deliberately NOT a stored
/// column -- it's computed in LeaseResponse at read time, the same "single source of
/// truth over a cached total" choice this codebase already made for other derived values.
/// </summary>
public class Lease : ITenantScopedEntity, IAuditableEntity, ISoftDelete
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PropertyId { get; set; }
    public Guid ResidentId { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public LeaseStatus Status { get; set; }

    public ICollection<LeaseRecurringCharge> RecurringCharges { get; set; } = new List<LeaseRecurringCharge>();

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public bool IsDeleted { get; set; }
}
