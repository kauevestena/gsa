using System.Text.Json;
using Gsa.Core.World;
using Gsa.Missions.Content;

namespace Gsa.Tools.Sky;

public static class SkyTool
{
    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length != 3 || args[0] != "precompute-sky")
        {
            Write(output, false, [new ContentDiagnostic("", "$command", "Usage: gsa-tools precompute-sky <scene-manifest> <output>")]);
            return 1;
        }
        var path = args[1];
        var field = "$";
        string? temporary = null;
        try
        {
            var input = Path.GetFullPath(args[1]);
            path = args[2];
            field = "$output";
            var target = Path.GetFullPath(args[2]);
            if (string.Equals(input, target, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("Output must not replace its input scene manifest.");
            path = input;
            field = "$";
            var manifest = WorldSceneManifest.Parse(File.ReadAllText(input));
            var cache = SkyPrecomputer.Compute(manifest);
            cache.ValidateFor(manifest);
            path = target;
            field = "$output";
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, cache.ToJson() + "\n");
            File.Move(temporary, target, overwrite: true);
            temporary = null;
            output.WriteLine(JsonSerializer.Serialize(new
            {
                valid = true, diagnostics = Array.Empty<ContentDiagnostic>(), scene_id = manifest.SceneId,
                map_version = manifest.MapVersion, geometry_sha256 = cache.GeometryHash,
                cells = cache.Cells.Count, output = target,
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
                                      FormatException or JsonException or NotSupportedException or OverflowException)
        {
            var reason = error.Message;
            if (error is FormatException && reason.StartsWith('$') && reason.IndexOf(':') is var separator && separator > 0)
            {
                field = reason[..separator];
                reason = reason[(separator + 1)..].Trim();
            }
            else if (error is JsonException jsonError)
                field = jsonError.Path ?? "$";
            Write(output, false, [new ContentDiagnostic(path, field, reason)]);
            return 2;
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void Write(TextWriter output, bool valid, IReadOnlyList<ContentDiagnostic> diagnostics) =>
        output.WriteLine(JsonSerializer.Serialize(new { valid, diagnostics },
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
}
