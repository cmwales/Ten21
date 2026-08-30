using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ten21.Business.UnitGroups;
using Ten21.Domain.Common;

namespace Ten21.Api.Controllers;

/// <summary>
/// US-29: workspace-scoped physical section/phase catalog backing the unit tiers/groups
/// matrix editor. Property Manager manages, Property Owner reads -- same Permissions.Property.*
/// claims the matrix and property list already use, no new permission category needed.
///
/// Business-layer refactor: all business logic AND all data access now live in
/// UnitGroupService (Ten21.Business) -- this controller has no Ten21DbContext dependency at
/// all.
/// </summary>
[ApiController]
[Route("api/unit-groups")]
public class UnitGroupsController : ControllerBase
{
    private readonly UnitGroupService _unitGroupService;

    public UnitGroupsController(UnitGroupService unitGroupService)
    {
        _unitGroupService = unitGroupService;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.Property.Read)]
    public async Task<IActionResult> GetUnitGroups(CancellationToken cancellationToken) =>
        Ok(await _unitGroupService.GetUnitGroupsAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Read)]
    public async Task<IActionResult> GetUnitGroup(Guid id, CancellationToken cancellationToken) =>
        Ok(await _unitGroupService.GetUnitGroupAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> CreateUnitGroup(
        [FromBody] UpsertUnitGroupRequest request, CancellationToken cancellationToken)
    {
        var response = await _unitGroupService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetUnitGroup), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> UpdateUnitGroup(
        Guid id, [FromBody] UpsertUnitGroupRequest request, CancellationToken cancellationToken) =>
        Ok(await _unitGroupService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> DeleteUnitGroup(Guid id, CancellationToken cancellationToken)
    {
        await _unitGroupService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
