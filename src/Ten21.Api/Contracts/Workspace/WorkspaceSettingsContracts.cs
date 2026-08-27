namespace Ten21.Api.Contracts.Workspace;

/// <summary>Refinement Sprint (Directive 4): the workspace-wide admin toggle set. Just one
/// field so far -- see WorkspaceSettings' own class comment on why this grows column-by-column
/// rather than as a speculative generic flags bag.</summary>
public record WorkspaceSettingsResponse(bool EnableCommunityDirectory);

public record UpdateWorkspaceSettingsRequest(bool EnableCommunityDirectory);
