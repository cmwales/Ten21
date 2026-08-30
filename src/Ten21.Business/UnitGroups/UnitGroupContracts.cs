namespace Ten21.Business.UnitGroups;

/// <summary>Business-layer refactor: relocated from Ten21.Api.Contracts.UnitGroups.</summary>
public record UpsertUnitGroupRequest(
    string GroupName,
    string? Description);

public record UnitGroupResponse(
    Guid Id,
    string GroupName,
    string? Description);
