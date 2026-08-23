using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>
/// Deliberately thin/temporary controller -- its only purpose right now is to give US-01 a
/// visible, callable proof that the isolation engine works end-to-end (JWT claim ->
/// TenantMiddleware -> ITenantContext -> EF Core query filter -> Postgres RLS).
///
/// This is NOT the real Properties API. Full property/unit CRUD, validation, and DTOs are
/// out of scope until DATA_MODEL Phase 3 work defines the full entity shape.
/// </summary>
[ApiController]
[Route("api/properties")]
public class PropertiesController : ControllerBase
{
    private readonly Ten21DbContext _dbContext;

    public PropertiesController(Ten21DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetProperties(CancellationToken cancellationToken)
    {
        // No manual .Where(p => p.TenantId == ...) anywhere in this method -- that's the
        // entire point. The global query filter in Ten21DbContext does it automatically,
        // and returns zero rows rather than every tenant's rows if no tenant is resolved.
        var properties = await _dbContext.Properties
            .Select(p => new
            {
                p.Id,
                p.StreetAddress,
                p.City,
                p.StateProvince,
                p.PostalCode
            })
            .ToListAsync(cancellationToken);

        return Ok(properties);
    }
}
