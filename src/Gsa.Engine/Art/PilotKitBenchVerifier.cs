using System.Text.Json;
using Godot;
using Gsa.Core.Art;
using Dictionary = Godot.Collections.Dictionary;

namespace Gsa.Engine.Art;

/// <summary>Verifies packaged pilot art kit glTF scenes, semantic extras, triangle counts, and atlas inside Godot runtime.</summary>
public static class PilotKitBenchVerifier
{
    public const string ManifestResPath = "res://world/pilot-kit/manifest.json";
    public const string AtlasResPath = "res://world/pilot-kit/textures/pilot_atlas_256.png";

    public static void RunVerificationSuite()
    {
        if (!Godot.FileAccess.FileExists(ManifestResPath))
            throw new InvalidOperationException($"Packaged pilot kit manifest missing at {ManifestResPath}.");

        var manifestJson = Godot.FileAccess.GetFileAsString(ManifestResPath);
        using var doc = JsonDocument.Parse(manifestJson);
        var root = doc.RootElement;

        var materials = root.GetProperty("materials")
            .EnumerateArray()
            .Select(m => m.GetProperty("id").GetString() ?? "")
            .ToHashSet(StringComparer.Ordinal);
        foreach (var reqMat in PilotArtKitValidator.RequiredTestMaterials)
        {
            if (!materials.Contains(reqMat))
                throw new InvalidOperationException($"Pilot kit manifest missing required material '{reqMat}'.");
        }

        var atlasTex = ResourceLoader.Load<Texture2D>(AtlasResPath);
        if (atlasTex is null)
            throw new InvalidOperationException($"Imported atlas texture missing at {AtlasResPath}.");
        var width = atlasTex.GetWidth();
        var height = atlasTex.GetHeight();
        if (width < PilotArtKitValidator.MinTexturePixels ||
            width > PilotArtKitValidator.MaxTexturePixels ||
            height < PilotArtKitValidator.MinTexturePixels ||
            height > PilotArtKitValidator.MaxTexturePixels)
        {
            throw new InvalidOperationException($"Atlas texture dimensions {width}x{height} violate budget.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var totalTris = 0;

        foreach (var mod in root.GetProperty("modules").EnumerateArray())
        {
            var id = mod.GetProperty("id").GetString()!;
            var expectedClass = mod.GetProperty("semantic_class").GetString()!;
            var expectedMat = mod.GetProperty("material").GetString()!;
            var expectedTris = mod.GetProperty("triangles").GetInt32();
            var relGltf = mod.GetProperty("gltf").GetString()!;
            if (expectedTris is < 50 or > 500)
                throw new InvalidOperationException($"Module '{id}' triangles {expectedTris} out of urban budget.");

            var gltfResPath = $"res://world/pilot-kit/{relGltf}";
            var scene = ResourceLoader.Load<PackedScene>(gltfResPath);
            if (scene is null)
                throw new InvalidOperationException($"Imported glTF scene missing at {gltfResPath}.");

            var instance = scene.Instantiate();
            try
            {
                var meshNode = FindFirstOfType<MeshInstance3D>(instance)
                               ?? throw new InvalidOperationException($"Imported glTF '{id}' has no MeshInstance3D.");
                var faces = meshNode.Mesh?.GetFaces()
                            ?? throw new InvalidOperationException($"Imported glTF '{id}' mesh has no faces.");
                var actualTris = faces.Length / 3;
                if (actualTris != expectedTris)
                    throw new InvalidOperationException($"Imported glTF '{id}' triangles {actualTris} != manifest {expectedTris}.");

                var extras = FindSemanticExtras(instance)
                             ?? throw new InvalidOperationException($"Imported glTF '{id}' lost glTF extras metadata.");
                if (!Matches(extras, "semantic_id", id) ||
                    !Matches(extras, "semantic_class", expectedClass) ||
                    !Matches(extras, "semantic_material", expectedMat))
                {
                    throw new InvalidOperationException($"Imported glTF '{id}' lost semantic_id/class/material metadata.");
                }
            }
            finally
            {
                instance.Free();
            }

            seen.Add(id);
            totalTris += expectedTris;
        }

        foreach (var reqMod in PilotArtKitValidator.RequiredModules)
        {
            if (!seen.Contains(reqMod))
                throw new InvalidOperationException($"Missing required pilot module '{reqMod}'.");
        }

        GD.Print($"GSA_PILOT_KIT_SMOKE_OK modules={seen.Count} materials={PilotArtKitValidator.RequiredTestMaterials.Count} " +
                 $"atlas={width}x{height} total_tris={totalTris}");
    }

    private static T? FindFirstOfType<T>(Node root) where T : Node
    {
        if (root is T match)
            return match;
        foreach (var child in root.GetChildren())
        {
            var found = FindFirstOfType<T>(child);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static Dictionary? FindSemanticExtras(Node root)
    {
        if (root.HasMeta("extras"))
        {
            var meta = root.GetMeta("extras");
            if (meta.VariantType == Variant.Type.Dictionary)
            {
                var dict = meta.AsGodotDictionary();
                if (dict.ContainsKey("semantic_id"))
                    return dict;
            }
        }
        foreach (var child in root.GetChildren())
        {
            var found = FindSemanticExtras(child);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static bool Matches(Dictionary dict, string key, string expected) =>
        dict.TryGetValue(key, out var val) && string.Equals(val.AsString(), expected, StringComparison.Ordinal);
}
