using Ten21.Domain.Common;

namespace Ten21.Domain.Entities;

/// <summary>
/// Tracks a presigned upload's metadata. The row is created at PRESIGN time (when the URL
/// is issued), not after a confirmed successful upload -- US-06's acceptance criteria call
/// for tracking "upload metadata," and confirming actual upload completion (e.g. via an S3
/// event webhook) is a reasonable future enhancement, not something to build speculatively
/// now without a concrete feature driving it.
/// </summary>
public class DocumentAttachment : ITenantScopedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string S3Key { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long ByteSize { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
