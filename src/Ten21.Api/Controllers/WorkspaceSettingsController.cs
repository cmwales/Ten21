using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ten21.Business.Workspace;
using Ten21.Domain.Common;

namespace Ten21.Api.Controllers;

/// <summary>
/// Refinement Sprint (Directive 4): the /admin/settings backend -- a single
/// WorkspaceSettings row per tenant, lazily created on first read rather than seeded at
/// tenant-provisioning time (see WorkspaceSettings' own class comment). Read/Write are
/// separate permissions so more roles can see the current toggle state than can change it;
/// see RolePermissions for exactly which roles get which.
///
/// Business-layer refactor: all business logic AND all data access now live in
/// WorkspaceSettingsService (Ten21.Business) -- this controller has no Ten21DbContext
/// dependency at all.
/// </summary>
[ApiController]
[Route("api/workspace/settings")]
public class WorkspaceSettingsController : ControllerBase
{
    private readonly WorkspaceSettingsService _workspaceSettingsService;

    public WorkspaceSettingsController(WorkspaceSettingsService workspaceSettingsService)
    {
        _workspaceSettingsService = workspaceSettingsService;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Workspace.SettingsRead)]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken) =>
        Ok(await _workspaceSettingsService.GetSettingsAsync(cancellationToken));

    [HttpPut]
    [Authorize(Policy = Permissions.Workspace.SettingsWrite)]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] UpdateWorkspaceSettingsRequest request, CancellationToken cancellationToken) =>
        Ok(await _workspaceSettingsService.UpdateSettingsAsync(request, cancellationToken));
}
