using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ten21.Domain.Common;

/// <summary>
/// Single source of truth for the JsonStringEnumConverter registration every JsonSerializer
/// call in this codebase should use -- both ASP.NET Core's own request/response pipeline
/// (Program.cs) and any JSON serialization done outside that pipeline (e.g.
/// AuditSaveChangesInterceptor's diff snapshots). Before this existed, only Program.cs
/// registered the converter, so AuditLog diffs serialized enums as their numeric underlying
/// value while every API request/response serialized the same enums as their string name --
/// see User_Stories_Sprint_3.md's "Flatten Property/Unit" addendum for the full history.
/// </summary>
public static class Ten21JsonOptions
{
    public static JsonStringEnumConverter CreateEnumConverter() => new();

    public static readonly JsonSerializerOptions Default = new()
    {
        Converters = { CreateEnumConverter() },
    };
}
