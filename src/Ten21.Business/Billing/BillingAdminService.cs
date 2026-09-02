using Microsoft.EntityFrameworkCore;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Billing;

/// <summary>
/// US-45 (Sprint 9): platform-level read access for the billing-cycle operational data --
/// Tenants and BillingCycleRun are both deliberately NOT ITenantScopedEntity (see their own
/// class comments), so these queries are never filtered by the EF global tenant filter the
/// way every other Business Service's queries are. Gated at the controller by
/// Permissions.Billing.ViewRuns (SuperAdmin only) or the internal API key, not by tenant
/// membership -- there is no "my own tenant" to scope these to.
/// </summary>
public class BillingAdminService
{
    private readonly Ten21DbContext _dbContext;

    public BillingAdminService(Ten21DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<TenantSummaryResponse>> ListTenantsAsync(CancellationToken cancellationToken) =>
        await _dbContext.Tenants.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new TenantSummaryResponse(t.Id, t.Name))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BillingCycleRunResponse>> ListRunsAsync(
        BillingCycleRunFilter filter, CancellationToken cancellationToken)
    {
        var query = _dbContext.BillingCycleRuns.AsNoTracking().AsQueryable();

        if (filter.Status is { } status)
        {
            query = query.Where(r => r.Status == status);
        }
        if (filter.TenantId is { } tenantId)
        {
            query = query.Where(r => r.TenantId == tenantId);
        }
        if (filter.FromDate is { } fromDate)
        {
            query = query.Where(r => r.RunDate >= fromDate);
        }
        if (filter.ToDate is { } toDate)
        {
            query = query.Where(r => r.RunDate <= toDate);
        }

        return await query
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new BillingCycleRunResponse(
                r.Id, r.TenantId, r.RunDate, r.Status, r.ErrorMessage, r.TriggeredBy, r.StartedAt, r.CompletedAt))
            .ToListAsync(cancellationToken);
    }
}
