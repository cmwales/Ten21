using Microsoft.EntityFrameworkCore;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Workspace;

/// <summary>Business-layer refactor: extracted from WorkspaceSettingsController. No
/// repository -- a single-table get-or-create, nothing batched. No interface -- same
/// reasoning as ChargeService/PaymentService.</summary>
public class WorkspaceSettingsService
{
    private readonly Ten21DbContext _dbContext;

    public WorkspaceSettingsService(Ten21DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WorkspaceSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateAsync(cancellationToken);
        return ToResponse(settings);
    }

    public async Task<WorkspaceSettingsResponse> UpdateSettingsAsync(
        UpdateWorkspaceSettingsRequest request, CancellationToken cancellationToken)
    {
        var settings = await GetOrCreateAsync(cancellationToken);
        settings.EnableCommunityDirectory = request.EnableCommunityDirectory;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(settings);
    }

    /// <summary>The unique index on TenantId (WorkspaceSettingsConfiguration) is what makes
    /// this safe under a race between two concurrent first-reads: the loser's insert throws
    /// DbUpdateException, at which point the row it was racing against is already there to
    /// re-fetch.</summary>
    private async Task<Domain.Entities.WorkspaceSettings> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        var existing = await _dbContext.WorkspaceSettings.FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var created = new Domain.Entities.WorkspaceSettings { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
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

    private static WorkspaceSettingsResponse ToResponse(Domain.Entities.WorkspaceSettings settings) =>
        new(settings.EnableCommunityDirectory);
}
