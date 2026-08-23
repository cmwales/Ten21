namespace Ten21.Infrastructure.Storage;

/// <summary>
/// Sanitizes a user-supplied string before it's embedded directly into an S3 object key
/// path. Not part of the literal US-06 wording, but a necessary defense-in-depth addition:
/// "Category" comes straight from client input and goes straight into
/// {TenantId}/{Category}/{EntityId}/{Guid}.ext -- without sanitizing, a crafted value like
/// "../other-tenant" could manipulate the resulting key structure. Extracted as its own
/// pure static method (rather than inlined in S3StorageService) specifically so it's
/// unit-testable without needing an S3 client at all.
/// </summary>
public static class ObjectKeySanitizer
{
    public static string SanitizeSegment(string value)
    {
        var cleaned = new string(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return string.IsNullOrEmpty(cleaned) ? "misc" : cleaned;
    }
}
