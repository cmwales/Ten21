namespace Ten21.Api.Contracts.ManualCharges;

/// <summary>Post-Sprint-6 fix: ResidentId removed -- charges/fines are billed to the unit,
/// not an individual occupant (see ManualCharge's own class comment). PaidDate added,
/// optional at create/update time so a charge can be logged as already-paid retroactively,
/// but its primary use is via the same PUT later once payment actually comes in.</summary>
public record UpsertManualChargeRequest(
    string Description,
    decimal Amount,
    DateOnly DueDate,
    string? AccountingCode,
    DateOnly? PaidDate = null);

public record ManualChargeResponse(
    Guid Id,
    Guid PropertyId,
    string Description,
    decimal Amount,
    DateOnly DueDate,
    string? AccountingCode,
    DateOnly? PaidDate);
