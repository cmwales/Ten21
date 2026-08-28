using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Ten21.Api.Contracts.Documents;
using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Api.Controllers;

/// <summary>US-06: Presigned Object Storage Service.</summary>
[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly IS3StorageService _storageService;
    private readonly Ten21DbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public DocumentsController(
        IS3StorageService storageService, Ten21DbContext dbContext, ITenantContext tenantContext)
    {
        _storageService = storageService;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    [HttpPost("presign-upload")]
    public async Task<IActionResult> PresignUpload(
        [FromBody] PresignUploadRequest request, CancellationToken cancellationToken)
    {
        // Validated BEFORE signing, per the acceptance criteria's own wording -- see
        // S3StorageService's class comment for the honest limitation on what "before
        // signing" can and can't actually guarantee about the eventual upload.
        if (!DocumentUploadPolicy.AllowedContentTypes.Contains(request.ContentType))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["ContentType"] = [$"Content type '{request.ContentType}' is not allowed."],
            });
        }

        if (request.ByteSize <= 0 || request.ByteSize > DocumentUploadPolicy.MaxByteSize)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["ByteSize"] =
                    [$"File size must be between 1 byte and {DocumentUploadPolicy.MaxByteSize / (1024 * 1024)}MB."],
            });
        }

        // Audit Refinement Sprint: strictly validate EntityId belongs to the caller's own
        // active tenant BEFORE signing -- see AnyTenantScopedRecordExistsAsync's own comment
        // for why this is a generic cross-table check rather than a Category-typed lookup.
        var entityBelongsToTenant = await _dbContext.AnyTenantScopedRecordExistsAsync(request.EntityId, cancellationToken);
        if (!entityBelongsToTenant)
        {
            throw new NotFoundException($"Entity '{request.EntityId}' was not found.");
        }

        // This action requires authentication (secure-by-default fallback policy), so
        // TenantContext.TenantId is guaranteed resolved here -- unlike AuthController's
        // login/refresh bootstrap cases, there's no IgnoreQueryFilters()-style exception
        // needed anywhere in this endpoint.
        var tenantId = _tenantContext.TenantId!.Value;
        var userId = Guid.Parse(User.FindFirst("user_id")!.Value);

        var presigned = _storageService.CreatePresignedUpload(
            tenantId, request.Category, request.EntityId, request.FileName, request.ContentType);

        _dbContext.DocumentAttachments.Add(new DocumentAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            S3Key = presigned.ObjectKey,
            FileName = request.FileName,
            ContentType = request.ContentType,
            ByteSize = request.ByteSize,
            UploadedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new PresignUploadResponse(presigned.UploadUrl, presigned.ObjectKey, presigned.ExpiresAtUtc));
    }
}
