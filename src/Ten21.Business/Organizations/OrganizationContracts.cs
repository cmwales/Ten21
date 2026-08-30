namespace Ten21.Business.Organizations;

/// <summary>Business-layer refactor: relocated from Ten21.Api.Contracts.Organization.</summary>
public record TenantMembershipSummary(Guid TenantId, string TenantName, bool IsPrimary, string Role);

public record SwitchContextRequest(Guid TenantId);

/// <summary>US-26: WorkspaceName/PortfolioSize mirror RegisterRequest's equivalent fields --
/// same shape as the self-registration workspace fields, just for an ALREADY-authenticated
/// Property Manager adding to their existing portfolio instead of creating their first
/// one.</summary>
public record AddWorkspaceRequest(string WorkspaceName, int PortfolioSize);

/// <summary>The result of a successful SwitchContext -- NewRawRefreshToken is deliberately
/// separate from the rest: it must be set as an HTTP-only cookie by the controller
/// (RefreshTokenCookie.Set), never returned in the JSON response body.</summary>
public record SwitchContextResult(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    Guid TenantId,
    Guid? OrganizationId,
    string Role,
    string NewRawRefreshToken);
