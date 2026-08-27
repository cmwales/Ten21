using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Workspace;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>
/// Refinement Sprint (Directive 4): the /admin/settings backend -- a single
/// WorkspaceSettings row per tenant, lazily created on first read rather than seeded at
/// tenant-provisioning time (see WorkspaceSettings' own class comment). Read/Write are
/// separate permissions so more roles can see the current toggle state than can change it;
/// see RolePermissions for exactly which roles get which.
/// </summary>
[ApiController]
[Route("api/workspace/settings")]
public class WorkspaceSettingsController : ControllerBase
{
    private readonly Ten21DbContext _dbContext;

    public WorkspaceSettingsController(Ten21DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Workspace.SettingsRead)]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateAsync(cancellationToken);
        return Ok(ToResponse(settings));
    }

    [HttpPut]
    [Authorize(Policy = Permissions.Workspace.SettingsWrite)]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] UpdateWorkspaceSettingsRequest request, CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateAsync(cancellationToken);
        settings.EnableCommunityDirectory = request.EnableCommunityDirectory;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(settings));
    }

    /// <summary>The unique index on TenantId (WorkspaceSettingsConfiguration) is what makes
    /// this safe under a race between two concurrent first-reads: the loser's insert throws
    /// DbUpdateException, at which point the row it was racing against is already there to
    /// re-fetch.</summary>
    private async Task<WorkspaceSettings> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        var existing = await _dbContext.WorkspaceSettings.FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = new WorkspaceSettings { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        _dbContext.WorkspaceSettings.Add(created);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return created;
        }
        catch (DbUpdateException)
        {
            _dbContext.Entry(created).State = EntityState.Detached;
            return await _dbContext.WorkspaceSettings.FirstAsync(cancellationToken);
        }
    }

    private static WorkspaceSettingsResponse ToResponse(WorkspaceSettings settings) =>
        new(settings.EnableCommunityDirectory);
}
