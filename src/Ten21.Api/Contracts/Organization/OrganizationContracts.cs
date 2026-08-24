namespace Ten21.Api.Contracts.Organization;

public record TenantMembershipSummary(Guid TenantId, string TenantName, bool IsPrimary, string Role);

public record SwitchContextRequest(Guid TenantId);

/// <summary>US-26: WorkspaceName/PortfolioSize mirror RegisterRequest's equivalent fields --
/// same shape as the self-registration workspace fields, just for an ALREADY-authenticated
/// Property Manager adding to their existing portfolio instead of creating their first
/// one.</summary>
public record AddWorkspaceRequest(string WorkspaceName, int PortfolioSize);
