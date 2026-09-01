using Ten21.Domain.Enums;

namespace Ten21.Business.Leases;

/// <summary>Business-layer refactor: relocated from Ten21.Api.Contracts.Leases so
/// LeaseService can accept/return these directly.
///
/// US-44 (Sprint 9): extended into the full recurring charge template shape -- base rent
/// is now just a request/response row with Category = BaseRent, same as every add-on.
/// See LeaseRecurringCharge's own class comment for field semantics.</summary>
public record LeaseRecurringChargeRequest(
    string ChargeName,
    ChargeCategory Category,
    decimal Amount,
    RecurrencePattern RecurrencePattern,
    EndStrategy EndStrategy,
    DateOnly EffectiveStartDate,
    ProrationStrategy ProrationStrategy,
    string? AccountingCode = null,
    string? Description = null,
    int RecurrenceInterval = 1,
    int? DueDayOfMonth = null,
    DayOfWeek? TargetDayOfWeek = null,
    int? SecondaryDueDay = null,
    DateOnly? EffectiveEndDate = null,
    bool IsPaused = false);

public record LeaseRecurringChargeResponse(
    Guid Id,
    string ChargeName,
    ChargeCategory Category,
    decimal Amount,
    RecurrencePattern RecurrencePattern,
    int RecurrenceInterval,
    int? DueDayOfMonth,
    DayOfWeek? TargetDayOfWeek,
    int? SecondaryDueDay,
    EndStrategy EndStrategy,
    DateOnly EffectiveStartDate,
    DateOnly? EffectiveEndDate,
    ProrationStrategy ProrationStrategy,
    bool IsPaused,
    string? AccountingCode,
    string? Description);

/// <summary>
/// US-30: RecurringCharges is always the FULL desired set for this lease -- PUT replaces
/// every existing charge row with whatever's in this list (remove-all-then-re-add), same
/// convention as UpsertResidentRequest.EmergencyContacts. US-44: must contain exactly one
/// Category = BaseRent row -- base rent is no longer a separate Lease field, it's just
/// this list's mandatory anchor entry.
/// </summary>
public record UpsertLeaseRequest(
    Guid ResidentId,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<LeaseRecurringChargeRequest> RecurringCharges,
    LeaseStatus Status = LeaseStatus.FixedTerm);

/// <summary>TotalMonthlyDues is computed at read time (Sum(RecurringCharges.Amount) for
/// every non-paused template currently within its effective window), never stored -- see
/// Lease's own class comment. A simplification for non-Monthly patterns: this sums each
/// active template's per-occurrence Amount as if it were a monthly figure, since this
/// codebase doesn't yet normalize e.g. a Weekly charge into a monthly-equivalent amount.
/// EffectiveStatus/IsExpiringSoon are US-32 additions, also computed at read time rather
/// than a background job -- Status stays the raw stored value so existing US-30 callers
/// reading it see no behavior change. The move-out notice that feeds
/// EffectiveStatus/IsExpiringSoon lives on Property now, not here -- see
/// PropertyResponse.MoveOutNoticeDate -- so it isn't duplicated onto every lease row.</summary>
public record LeaseResponse(
    Guid Id,
    Guid PropertyId,
    Guid ResidentId,
    DateOnly StartDate,
    DateOnly EndDate,
    LeaseStatus Status,
    decimal TotalMonthlyDues,
    IReadOnlyList<LeaseRecurringChargeResponse> RecurringCharges,
    LeaseStatus EffectiveStatus,
    bool IsExpiringSoon);

/// <summary>US-32: the "Create Move-In Charge" action -- MoveInDate is the resident's actual
/// move-in day (usually, but not necessarily, the same as the lease's BaseRent template's
/// EffectiveStartDate), used to compute the partial-period amount owed before the lease's
/// regular billing cycle begins.</summary>
public record CreateMoveInChargeRequest(DateOnly MoveInDate);
