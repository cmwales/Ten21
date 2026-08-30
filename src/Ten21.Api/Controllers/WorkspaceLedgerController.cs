using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ten21.Business.Workspace;
using Ten21.Domain.Common;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-36: the workspace-wide ledger rollup -- a pure reporting aggregation over every
/// property the caller's tenant manages, using the exact same Charge/PaymentTransaction/
/// ChargeAdjustment rows and balance formula as ChargesController.GetStatement's per-property
/// unit statement (US-33). No new tables; this only reads. Not nested under
/// api/properties/{propertyId} since it isn't scoped to one property -- it's the rollup
/// above that level, at api/workspace/ledger.
///
/// Business-layer refactor: all data access now lives in WorkspaceLedgerService
/// (Ten21.Business) -- this controller has no Ten21DbContext dependency at all.
/// </summary>
[ApiController]
[Route("api/workspace/ledger")]
public class WorkspaceLedgerController : ControllerBase
{
    private readonly WorkspaceLedgerService _workspaceLedgerService;

    public WorkspaceLedgerController(WorkspaceLedgerService workspaceLedgerService)
    {
        _workspaceLedgerService = workspaceLedgerService;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Ledger.Read)]
    public async Task<IActionResult> GetWorkspaceLedger(CancellationToken cancellationToken) =>
        Ok(await _workspaceLedgerService.GetWorkspaceLedgerAsync(cancellationToken));
}
