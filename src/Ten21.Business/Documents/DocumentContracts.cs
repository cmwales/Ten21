namespace Ten21.Business.Documents;

/// <summary>Business-layer refactor: relocated from Ten21.Api.Contracts.Documents so
/// DocumentService can accept/return these directly.</summary>
public record PresignUploadRequest(string Category, Guid EntityId, string FileName, string ContentType, long ByteSize);

public record PresignUploadResponse(string UploadUrl, string S3Key, DateTimeOffset ExpiresAtUtc);
