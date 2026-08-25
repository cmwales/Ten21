namespace Ten21.Api.Contracts.ManualCharges;

public record UpsertManualChargeRequest(
    Guid? ResidentId,
    string Description,
    decimal Amount,
    DateOnly DueDate,
    string? AccountingCode);

public record ManualChargeResponse(
    Guid Id,
    Guid PropertyId,
    Guid? ResidentId,
    string Description,
    decimal Amount,
    DateOnly DueDate,
    string? AccountingCode);
