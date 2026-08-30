using Microsoft.AspNetCore.Mvc;
using Ten21.Business.Documents;

namespace Ten21.Api.Controllers;

/// <summary>US-06: Presigned Object Storage Service.
///
/// Business-layer refactor: all business logic AND all data access now live in
/// DocumentService (Ten21.Business) -- this controller has no Ten21DbContext dependency at
/// all. It only extracts the caller's user_id claim (an ASP.NET Core-specific concern) and
/// delegates.
/// </summary>
[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly DocumentService _documentService;

    public DocumentsController(DocumentService documentService)
    {
        _documentService = documentService;
    }

    [HttpPost("presign-upload")]
    public async Task<IActionResult> PresignUpload(
        [FromBody] PresignUploadRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirst("user_id")!.Value);
        return Ok(await _documentService.PresignUploadAsync(request, userId, cancellationToken));
    }
}
