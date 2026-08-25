namespace Ten21.Api.Contracts.UnitGroups;

public record UpsertUnitGroupRequest(
    string GroupName,
    string? Description);

public record UnitGroupResponse(
    Guid Id,
    string GroupName,
    string? Description);
