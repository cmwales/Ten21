namespace Ten21.Api.Contracts.Organization;

public record TenantMembershipSummary(Guid TenantId, string TenantName, bool IsPrimary, string Role);

public record SwitchContextRequest(Guid TenantId);
