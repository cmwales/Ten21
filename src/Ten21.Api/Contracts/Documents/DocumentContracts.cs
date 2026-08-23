namespace Ten21.Api.Contracts.Documents;

public record PresignUploadRequest(string Category, Guid EntityId, string FileName, string ContentType, long ByteSize);

public record PresignUploadResponse(string UploadUrl, string S3Key, DateTimeOffset ExpiresAtUtc);
