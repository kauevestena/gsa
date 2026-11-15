using System.Text.Json.Nodes;
using Gsa.Core.Art;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class ArtKitTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gsa.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    public void RepositoryPilotArtKitPassesBudgetsMaterialsAndSemanticChecks()
    {
        var kitDir = Path.Combine(FindRepoRoot(), "game", "world", "pilot-kit");
        var summary = PilotArtKitValidator.ValidateDirectory(kitDir);

        Assert.True(summary.Valid, string.Join("\n", summary.Diagnostics.Select(d => $"{d.Path}:{d.Field}:{d.Reason}")));
        Assert.Equal(9, summary.Modules.Count);
        foreach (var req in PilotArtKitValidator.RequiredModules)
            Assert.Contains(summary.Modules, m => m.ModuleId == req);
        foreach (var mat in PilotArtKitValidator.RequiredTestMaterials)
            Assert.Contains(mat, summary.Materials);

        Assert.Equal(256, summary.AtlasWidth);
        Assert.Equal(256, summary.AtlasHeight);
        Assert.All(summary.Modules, m => Assert.InRange(m.TriangleCount, 50, 500));
    }

    [Fact]
    public void ValidatorRejectsSemanticLossBudgetViolationsAndRealBrands()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"gsa-art-invalid-{Guid.NewGuid():N}");
        var srcDir = Path.Combine(FindRepoRoot(), "game", "world", "pilot-kit");
        try
        {
            CopyDirectory(srcDir, tempDir);

            // Corrupt facade glTF to lose semantic_class and inject a forbidden real brand name.
            var facadeGltfPath = Path.Combine(tempDir, "modules", "facade.gltf");
            var gltfNode = JsonNode.Parse(File.ReadAllText(facadeGltfPath))!.AsObject();
            var extras = gltfNode["nodes"]![0]!["extras"]!.AsObject();
            extras["semantic_class"] = "";
            extras["brand"] = "Toyota City Props";
            File.WriteAllText(facadeGltfPath, gltfNode.ToJsonString());

            // Corrupt pole entry category budget mismatch (urban <= 500 vs 2000 simulated by modifying accessor count).
            var poleGltfPath = Path.Combine(tempDir, "modules", "pole.gltf");
            var poleNode = JsonNode.Parse(File.ReadAllText(poleGltfPath))!.AsObject();
            poleNode["accessors"]![3]!["count"] = 1800; // 600 tris > 500
            File.WriteAllText(poleGltfPath, poleNode.ToJsonString());

            var summary = PilotArtKitValidator.ValidateDirectory(tempDir);
            Assert.False(summary.Valid);
            Assert.Contains(summary.Diagnostics, d => d.Field == "$.nodes[0].extras.semantic_class");
            Assert.Contains(summary.Diagnostics, d => d.Reason.Contains("toyota", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(summary.Diagnostics, d => d.Reason.Contains("violates urban budget", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}

