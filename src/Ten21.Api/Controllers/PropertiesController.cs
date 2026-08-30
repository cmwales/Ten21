using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ten21.Business.Properties;
using Ten21.Domain.Common;

namespace Ten21.Api.Controllers;

/// <summary>
/// The real Property CRUD surface, replacing the throwaway US-01 proof-of-concept this
/// controller used to be. Property is a flat, standalone leasable space -- a whole
/// single-family house, or one suite within a larger building -- with no separate child
/// Unit entity. An earlier design (US-19-22) had Property own a collection of child Units;
/// tester feedback reversed that: "Each suite in a building needs to be a new property.
/// They need to be setup independently." See User_Stories_Sprint_3.md's "Flatten
/// Property/Unit" addendum for the full history.
///
/// Business-layer refactor: all business logic AND all data access now live in
/// PropertyService (Ten21.Business) -- this controller has no Ten21DbContext dependency at
/// all. It only routes/authorizes and delegates.
/// </summary>
[ApiController]
[Route("api/properties")]
public class PropertiesController : ControllerBase
{
    private readonly PropertyService _propertyService;

    public PropertiesController(PropertyService propertyService)
    {
        _propertyService = propertyService;
    }

    /// <summary>
    /// pageNumber/pageSize are both optional. Omitting pageSize returns every property,
    /// unpaginated -- the Angular list view does its own client-side search/pagination over
    /// that full set, so this is what the frontend actually calls; pageNumber/pageSize exist
    /// for direct API consumers that want real server-side paging.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Permissions.Property.Read)]
    public async Task<IActionResult> GetProperties(
        [FromQuery] int? pageNumber, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await _propertyService.GetPropertiesAsync(pageNumber, pageSize, cancellationToken));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Read)]
    public async Task<IActionResult> GetProperty(Guid id, CancellationToken cancellationToken) =>
        Ok(await _propertyService.GetPropertyAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> CreateProperty(
        [FromBody] UpsertPropertyRequest request, CancellationToken cancellationToken)
    {
        var response = await _propertyService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetProperty), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> UpdateProperty(
        Guid id, [FromBody] UpsertPropertyRequest request, CancellationToken cancellationToken) =>
        Ok(await _propertyService.UpdateAsync(id, request, cancellationToken));

    /// <summary>
    /// Post-Sprint-6 fix: the PM sets/clears this from the Lease drawer when they learn a
    /// unit is (or isn't) about to be vacated -- see Property.MoveOutNoticeDate's own doc
    /// comment for why this is a property-level fact, not a per-lease/resident one.
    /// </summary>
    [HttpPatch("{id:guid}/move-out-notice")]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> UpdateMoveOutNotice(
        Guid id, [FromBody] UpdateMoveOutNoticeRequest request, CancellationToken cancellationToken) =>
        Ok(await _propertyService.UpdateMoveOutNoticeAsync(id, request, cancellationToken));

    /// <summary>
    /// US-29: single-row matrix auto-save (blur/change). Full replace of the three matrix
    /// columns, same "send the current full row state" convention as UpdateProperty.
    /// </summary>
    [HttpPatch("{id:guid}/matrix")]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> UpdatePropertyMatrixRow(
        Guid id, [FromBody] UpdatePropertyMatrixRowRequest request, CancellationToken cancellationToken) =>
        Ok(await _propertyService.UpdateMatrixRowAsync(id, request, cancellationToken));

    /// <summary>
    /// US-29: the matrix editor's batch toolbar -- applies one UnitGroup or UnitTier to
    /// every checked row in a single request.
    /// </summary>
    [HttpPatch("matrix/batch")]
    [Authorize(Policy = Permissions.Property.Manage)]
    public async Task<IActionResult> BatchAssignMatrix(
        [FromBody] BatchAssignMatrixRequest request, CancellationToken cancellationToken) =>
        Ok(await _propertyService.BatchAssignMatrixAsync(request, cancellationToken));

    /// <summary>
    /// US-22: zero applied payments -> a genuine hard delete; applied payments -> a soft
    /// delete. See PropertyService.DeleteAsync for the full reasoning.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Property.Delete)]
    public async Task<IActionResult> DeleteProperty(Guid id, CancellationToken cancellationToken)
    {
        await _propertyService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// US-21: parses, sanitizes, and validates every row up front -- nothing is added to the
    /// DbContext until every row has passed. See PropertyService.ImportAsync for the full
    /// atomic-rollback reasoning. The buffer is never written to disk or object storage --
    /// IFormFile.OpenReadStream() is parsed directly from request memory and discarded once
    /// this action returns.
    /// </summary>
    [HttpPost("import")]
    [Authorize(Policy = Permissions.Property.Import)]
    [RequestSizeLimit(PropertyService.MaxImportFileSizeBytes)]
    public async Task<IActionResult> ImportProperties(IFormFile? file, CancellationToken cancellationToken) =>
        Ok(await _propertyService.ImportAsync(file, cancellationToken));
}
