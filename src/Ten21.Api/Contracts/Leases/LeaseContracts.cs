using Ten21.Domain.Enums;

namespace Ten21.Api.Contracts.Leases;

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
    DateOnly? MoveOutNoticeDate,
    IReadOnlyList<LeaseRecurringChargeRequest> RecurringCharges,
    LeaseStatus Status = LeaseStatus.FixedTerm);

/// <summary>TotalMonthlyDues is computed at read time (MonthlyBaseRent + Sum(RecurringCharges)),
/// never stored -- see Lease's own class comment.</summary>
public record LeaseResponse(
    Guid Id,
    Guid PropertyId,
    Guid ResidentId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal MonthlyBaseRent,
    int DueDayOfMonth,
    LeaseStatus Status,
    DateOnly? MoveOutNoticeDate,
    decimal TotalMonthlyDues,
    IReadOnlyList<LeaseRecurringChargeResponse> RecurringCharges);
