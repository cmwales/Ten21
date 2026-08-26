using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Api.Contracts.Workspace;
using Ten21.Domain.Common;
using Ten21.Domain.Enums;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-36: the workspace-wide ledger rollup -- a pure reporting aggregation over every
/// property the caller's tenant manages, using the exact same Charge/PaymentTransaction/
/// ChargeAdjustment rows and balance formula as ChargesController.GetStatement's per-property
/// unit statement (US-33). No new tables; this only reads. Not nested under
/// api/properties/{propertyId} since it isn't scoped to one property -- it's the rollup
/// above that level, at api/workspace/ledger.
/// </summary>
[ApiController]
[Route("api/workspace/ledger")]
public class WorkspaceLedgerController : ControllerBase
{
    private readonly Ten21DbContext _dbContext;

    public WorkspaceLedgerController(Ten21DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetWorkspaceLedger(CancellationToken cancellationToken)
    {
        var properties = await _dbContext.Properties
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.UnitIdentifier })
            .ToListAsync(cancellationToken);
        var propertyIds = properties.Select(p => p.Id).ToList();

        var charges = await _dbContext.Charges
            .Where(c => propertyIds.Contains(c.PropertyId))
            .Select(c => new { c.Id, c.PropertyId, c.Amount, c.Status })
            .ToListAsync(cancellationToken);
        var chargeIds = charges.Select(c => c.Id).ToList();

        var payments = await _dbContext.PaymentTransactions
            .Where(p => propertyIds.Contains(p.PropertyId))
            .Select(p => new { p.PropertyId, p.AmountPaid })
            .ToListAsync(cancellationToken);

        var adjustments = await _dbContext.ChargeAdjustments
            .Where(a => chargeIds.Contains(a.TargetChargeId))
            .Select(a => new { a.TargetChargeId, a.AdjustmentType, a.Amount })
            .ToListAsync(cancellationToken);

        // Same formula as UnitStatementResponse's own comment: SumActiveCharges + SumDebits -
        // SumPayments - SumCredits, computed per property then rolled up into TotalBalance.
        var summaries = properties.Select(property =>
        {
            var propertyChargeIds = charges.Where(c => c.PropertyId == property.Id).Select(c => c.Id).ToHashSet();

            var sumActiveCharges = charges
                .Where(c => c.PropertyId == property.Id && c.Status == ChargeLifecycleStatus.Active)
                .Sum(c => c.Amount);
            var sumDebits = adjustments
                .Where(a => propertyChargeIds.Contains(a.TargetChargeId) && a.AdjustmentType == AdjustmentType.DebitAdjustment)
                .Sum(a => a.Amount);
            var sumCredits = adjustments
                .Where(a => propertyChargeIds.Contains(a.TargetChargeId) && a.AdjustmentType == AdjustmentType.CreditAdjustment)
                .Sum(a => a.Amount);
            var sumPayments = payments.Where(p => p.PropertyId == property.Id).Sum(p => p.AmountPaid);

            var balance = sumActiveCharges + sumDebits - sumPayments - sumCredits;
            return new PropertyLedgerSummaryResponse(property.Id, property.Name, property.UnitIdentifier, balance);
        }).ToList();

        return Ok(new WorkspaceLedgerResponse(summaries.Sum(s => s.Balance), summaries));
    }
}
