namespace Ten21.Application.Abstractions;

/// <summary>
/// Generates presigned PUT URLs for direct-to-object-storage uploads (S3 or R2, which is
/// S3-API-compatible). Interfaced because the concrete provider (S3 vs R2 vs something
/// else later) is exactly the kind of thing TECH_PREFERENCES already treats as a live
/// choice ("AWS S3 / Cloudflare R2"), and because tests need a seam that doesn't require
/// real cloud credentials.
/// </summary>
public interface IS3StorageService
{
    PresignedUpload CreatePresignedUpload(
        Guid tenantId, string category, Guid entityId, string fileName, string contentType);
}

public record PresignedUpload(string UploadUrl, string ObjectKey, DateTimeOffset ExpiresAtUtc);
