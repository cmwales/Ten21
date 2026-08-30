using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ten21.Business.UnitTiers;
using Ten21.Domain.Common;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-29: workspace-scoped pricing tier catalog backing the unit tiers/groups matrix editor.
/// Property Manager manages, Property Owner reads -- same Permissions.Property.* claims the
/// matrix and property list already use, no new permission category needed.
///
/// Business-layer refactor: all business logic AND all data access now live in
/// UnitTierService (Ten21.Business) -- this controller has no Ten21DbContext dependency at
/// all.
/// </summary>
[ApiController]
[Route("api/unit-tiers")]
public class UnitTiersController : ControllerBase
{
    private readonly UnitTierService _unitTierService;

    public UnitTiersController(UnitTierService unitTierService)
    {
        _unitTierService = unitTierService;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Property.Read)]
    public async Task<IActionResult> GetUnitTiers(CancellationToken cancellationToken) =>
        Ok(await _unitTierService.GetUnitTiersAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Read)]
    public async Task<IActionResult> GetUnitTier(Guid id, CancellationToken cancellationToken) =>
        Ok(await _unitTierService.GetUnitTierAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> CreateUnitTier(
        [FromBody] UpsertUnitTierRequest request, CancellationToken cancellationToken)
    {
        var response = await _unitTierService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetUnitTier), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> UpdateUnitTier(
        Guid id, [FromBody] UpsertUnitTierRequest request, CancellationToken cancellationToken) =>
        Ok(await _unitTierService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> DeleteUnitTier(Guid id, CancellationToken cancellationToken)
    {
        await _unitTierService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
