using System.Text.Json;
using Gsa.Missions.Content;

namespace Gsa.Tools;

public static class ToolApplication
{
    public static int Run(string[] args, TextWriter output)
    {
        if (args.Length > 0 && args[0] == "precompute-sky")
            return Sky.SkyTool.Run(args, output);

        if (args.Length != 2 || args[0] != "validate-content")
        {
            Write(output, false, [new ContentDiagnostic("", "$command", "Usage: gsa-tools validate-content <directory> | precompute-sky <scene-manifest> <output>")]);
            return 1;
        }

        var diagnostics = ContentDirectoryValidator.Validate(args[1]);
        Write(output, diagnostics.Count == 0, diagnostics);
        return diagnostics.Count == 0 ? 0 : 2;
    }

    private static void Write(TextWriter output, bool valid, IReadOnlyList<ContentDiagnostic> diagnostics) =>
        output.WriteLine(JsonSerializer.Serialize(new { valid, diagnostics },
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
}
