using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Ten21.Application.Abstractions;

namespace Ten21.Infrastructure.Storage;

/// <summary>
/// Generates 15-minute presigned PUT URLs (US-06) using the AWS SDK's S3 client -- which
/// also works against Cloudflare R2 unchanged, since R2 is deliberately S3-API-compatible
/// (TECH_PREFERENCES lists both as live options). Which one you're pointed at is purely an
/// AmazonS3Config.ServiceURL setting, wired in ObjectStorageServiceCollectionExtensions.
///
/// IMPORTANT LIMITATION, stated plainly rather than implied by the code: generating a
/// presigned PUT URL is a pure local signing operation -- it never contacts S3/R2 at
/// generation time, and by itself it does NOT cryptographically enforce the 10MB ceiling
/// on the actual upload. The size validation (DocumentUploadPolicy.MaxByteSize) only
/// checks the CLIENT-DECLARED byte size in the presign request, before signing -- a client
/// could still request a valid presigned URL with an honest declared size and then upload
/// more bytes than declared, since a basic presigned PUT doesn't bind Content-Length the
/// way a presigned POST with policy conditions can. If this needs to be airtight later, the
/// options are: switch to presigned POST with a content-length-range policy condition, add
/// a bucket lifecycle/Lambda validation step, or enforce object size server-side after
/// upload completes (e.g. via an S3 event notification). Flagging honestly now rather than
/// letting "10MB limit" read as a stronger guarantee than what's actually implemented.
/// </summary>
public class S3StorageService : IS3StorageService
{
    private static readonly TimeSpan PresignExpiry = TimeSpan.FromMinutes(15);

    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;

    public S3StorageService(IAmazonS3 s3Client, IConfiguration configuration)
    {
        _s3Client = s3Client;
        _bucketName = configuration["ObjectStorage:BucketName"]
            ?? throw new InvalidOperationException("ObjectStorage:BucketName is not configured.");
    }

    public PresignedUpload CreatePresignedUpload(
        Guid tenantId, string category, Guid entityId, string fileName, string contentType)
    {
        var sanitizedCategory = ObjectKeySanitizer.SanitizeSegment(category);
        var extension = Path.GetExtension(fileName);
        var objectKey = $"{tenantId}/{sanitizedCategory}/{entityId}/{Guid.NewGuid()}{extension}";

        var expiresAtUtc = DateTimeOffset.UtcNow.Add(PresignExpiry);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Expires = expiresAtUtc.UtcDateTime,
            ContentType = contentType,
        };

        var uploadUrl = _s3Client.GetPreSignedURL(request);
        return new PresignedUpload(uploadUrl, objectKey, expiresAtUtc);
    }
}
