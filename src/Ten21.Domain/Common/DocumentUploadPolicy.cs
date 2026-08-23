namespace Ten21.Domain.Common;

/// <summary>
/// Upload validation policy for presigned object storage (US-06). Kept as plain constants
/// in Domain, not embedded in the controller or the storage service, so both can reference
/// the same source of truth.
/// </summary>
public static class DocumentUploadPolicy
{
    public const long MaxByteSize = 10 * 1024 * 1024; // 10MB, per US-06 acceptance criteria

    public static readonly IReadOnlyList<string> AllowedContentTypes =
    [
        "image/jpeg",
        "image/png",
        "image/webp",
        "application/pdf",
    ];
}
