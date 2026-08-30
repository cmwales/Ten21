namespace Ten21.Business.UnitTiers;

/// <summary>Business-layer refactor: relocated from Ten21.Api.Contracts.UnitTiers.</summary>
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
