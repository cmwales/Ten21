namespace Ten21.Api.Contracts.UnitTiers;

public record UpsertUnitTierRequest(
    string TierName,
    decimal DefaultRent,
    string? AccountingCode,
    string? Description);

public record UnitTierResponse(
    Guid Id,
    string TierName,
    decimal DefaultRent,
    string? AccountingCode,
    string? Description);
