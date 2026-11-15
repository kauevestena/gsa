using System.Text.Json;
using Gsa.Core.Art;
using Gsa.Missions.Content;

namespace Gsa.Tools.Art;

public static class ArtKitTool
{
    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length != 2 || args[0] != "validate-art-kit")
        {
            var usage = new[] { new ContentDiagnostic("", "$command", "Usage: gsa-tools validate-art-kit <directory>") };
            output.WriteLine(JsonSerializer.Serialize(new { valid = false, diagnostics = usage },
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
            return 1;
        }

        var fullPath = Path.GetFullPath(args[1]);
        var summary = PilotArtKitValidator.ValidateDirectory(fullPath);
        var diagnostics = summary.Diagnostics
            .Select(d => new ContentDiagnostic(d.Path, d.Field, d.Reason))
            .ToArray();

        output.WriteLine(JsonSerializer.Serialize(new
        {
            valid = summary.Valid,
            diagnostics,
            directory = fullPath,
            modules = summary.Modules,
            materials = summary.Materials,
            total_triangles = summary.TotalTriangles,
            atlas_width = summary.AtlasWidth,
            atlas_height = summary.AtlasHeight,
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));

        return summary.Valid ? 0 : 2;
    }
}

