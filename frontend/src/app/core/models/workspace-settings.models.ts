/** Mirrors Ten21.Api.Contracts.Workspace.WorkspaceSettingsResponse (Refinement Sprint,
 * Directive 4). Just one toggle so far -- see WorkspaceSettings' own backend comment. */
export interface WorkspaceSettingsResponse {
  enableCommunityDirectory: boolean;
}

/** Mirrors Ten21.Api.Contracts.Workspace.UpdateWorkspaceSettingsRequest. */
export interface UpdateWorkspaceSettingsRequest {
  enableCommunityDirectory: boolean;
}
