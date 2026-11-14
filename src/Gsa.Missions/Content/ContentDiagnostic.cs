using System.Text.Json.Serialization;

namespace Gsa.Missions.Content;

/// <summary>A developer-facing content error with a source file and an exact JSON field.</summary>
public sealed record ContentDiagnostic(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("reason")] string Reason);
