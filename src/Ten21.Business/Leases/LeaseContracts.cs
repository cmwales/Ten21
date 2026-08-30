using Ten21.Domain.Enums;

namespace Ten21.Business.Leases;

/// <summary>Business-layer refactor: relocated from Ten21.Api.Contracts.Leases so
/// LeaseService can accept/return these directly.</summary>
public record LeaseRecurringChargeRequest(string ChargeName, decimal Amount, string? AccountingCode);

public record LeaseRecurringChargeResponse(Guid Id, string ChargeName, decimal Amount, string? AccountingCode);

/// <summary>
/// US-30: RecurringCharges is always the FULL desired set for this lease -- PUT replaces
/// every existing charge row with whatever's in this list (remove-all-then-re-add), same
/// convention as UpsertResidentRequest.EmergencyContacts.
/// </summary>
public record UpsertLeaseRequest(
    Guid ResidentId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal MonthlyBaseRent,
    int DueDayOfMonth,
    IReadOnlyList<LeaseRecurringChargeRequest> RecurringCharges,
    LeaseStatus Status = LeaseStatus.FixedTerm);

/// <summary>TotalMonthlyDues is computed at read time (MonthlyBaseRent + Sum(RecurringCharges)),
/// never stored -- see Lease's own class comment. EffectiveStatus/IsExpiringSoon are US-32
/// additions, also computed at read time rather than a background job -- Status stays the raw
/// stored value so existing US-30 callers reading it see no behavior change. The move-out
/// notice that feeds EffectiveStatus/IsExpiringSoon lives on Property now, not here -- see
/// PropertyResponse.MoveOutNoticeDate -- so it isn't duplicated onto every lease row.</summary>
public record LeaseResponse(
    Guid Id,
    Guid PropertyId,
    Guid ResidentId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal MonthlyBaseRent,
    int DueDayOfMonth,
    LeaseStatus Status,
    decimal TotalMonthlyDues,
    IReadOnlyList<LeaseRecurringChargeResponse> RecurringCharges,
    LeaseStatus EffectiveStatus,
    bool IsExpiringSoon);

/// <summary>US-32: the "Create Move-In Charge" action -- MoveInDate is the resident's actual
/// move-in day (usually, but not necessarily, the same as Lease.StartDate), used to compute
/// the partial-period amount owed before the lease's regular DueDayOfMonth billing cycle
/// begins.</summary>
public record CreateMoveInChargeRequest(DateOnly MoveInDate);
