using System.Text.Json;

namespace Gsa.Missions.Content;

/// <summary>Compatibility entry point for exact JSON integer parsing shared by content modules.</summary>
public static class JsonNumbers
{
    public static bool TryGetInt64(JsonElement value, out long result) =>
        Gsa.Core.Serialization.JsonNumbers.TryGetInt64(value, out result);
}
