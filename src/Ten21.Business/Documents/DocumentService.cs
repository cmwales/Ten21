using Ten21.Application.Abstractions;
using Ten21.Domain.Common;
using Ten21.Domain.Entities;
using Ten21.Domain.Exceptions;
using Ten21.Infrastructure.Persistence;

namespace Ten21.Business.Documents;

/// <summary>US-06: Presigned Object Storage Service, extracted from DocumentsController.
/// userId is passed in rather than read from a ClaimsPrincipal here -- extracting it from the
/// HTTP request's claims is an ASP.NET Core-specific concern that stays in the controller,
/// same split as the resource-authorization convention elsewhere in this codebase. No
/// repository -- the one query here (AnyTenantScopedRecordExistsAsync) is a single
/// already-shared extension method, not something this service's own data-access class would
/// add value wrapping. No interface -- same reasoning as ChargeService/PaymentService.</summary>
public class DocumentService
{
    private readonly IS3StorageService _storageService;
    private readonly Ten21DbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public DocumentService(IS3StorageService storageService, Ten21DbContext dbContext, ITenantContext tenantContext)
    {
        _storageService = storageService;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<PresignUploadResponse> PresignUploadAsync(
        PresignUploadRequest request, Guid userId, CancellationToken cancellationToken)
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

        return new PresignUploadResponse(presigned.UploadUrl, presigned.ObjectKey, presigned.ExpiresAtUtc);
    }
}
