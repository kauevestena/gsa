using System.Buffers.Binary;
using System.Text.Json;

namespace Gsa.Core.Art;

public sealed record ArtDiagnostic(string Path, string Field, string Reason);

public sealed record ArtModuleValidationReport(
    string ModuleId,
    string SemanticClass,
    string Material,
    string AssetCategory,
    int TriangleCount,
    string GltfRelativePath);

public sealed record ArtKitValidationSummary(
    bool Valid,
    IReadOnlyList<ArtDiagnostic> Diagnostics,
    IReadOnlyList<ArtModuleValidationReport> Modules,
    IReadOnlyList<string> Materials,
    int TotalTriangles,
    int AtlasWidth,
    int AtlasHeight);

/// <summary>Validates pilot glTF 2.0 modular art kit, atlas bounds, triangle budgets, and semantic preservation.</summary>
public static class PilotArtKitValidator
{
    public static readonly IReadOnlyList<string> RequiredModules =
        ["lot", "sidewalk", "corner", "intersection", "facade", "pole", "tree", "sign", "manhole"];

    public static readonly IReadOnlyList<string> RequiredTestMaterials =
        ["concrete", "glass", "water"];

    private static readonly IReadOnlyDictionary<string, (int MinTris, int MaxTris)> CategoryBudgets =
        new Dictionary<string, (int, int)>(StringComparer.Ordinal)
        {
            ["urban"] = (50, 500),
            ["vehicle"] = (1500, 4000),
            ["character"] = (1000, 2500),
        };

    private static readonly string[] ForbiddenRealBrands =
    [
        "toyota", "ford", "chevrolet", "volkswagen", "mercedes", "bmw", "honda",
        "hyundai", "renault", "fiat", "coca-cola", "pepsi", "sony", "apple",
        "nikon", "canon", "leica", "trimble", "velodyne", "ouster", "hesai",
        "tesla", "byd"
    ];

    public const int MinTexturePixels = 128;
    public const int MaxTexturePixels = 512;

    public static ArtKitValidationSummary ValidateDirectory(string directory)
    {
        var diagnostics = new List<ArtDiagnostic>();
        var reports = new List<ArtModuleValidationReport>();
        var materials = new List<string>();
        var atlasWidth = 0;
        var atlasHeight = 0;

        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                diagnostics.Add(new ArtDiagnostic(directory ?? "", "$", "Pilot art kit directory does not exist."));
                return new ArtKitValidationSummary(false, diagnostics, reports, materials, 0, 0, 0);
            }
            if (IsSymlink(directory))
            {
                diagnostics.Add(new ArtDiagnostic(directory, "$", "Symbolic links are not permitted in art kit."));
                return new ArtKitValidationSummary(false, diagnostics, reports, materials, 0, 0, 0);
            }

            var rootFull = Path.GetFullPath(directory);
            var manifestPath = Path.Combine(rootFull, "manifest.json");
            var presetPath = Path.Combine(rootFull, "blender-gltf-export-preset.json");

            if (!File.Exists(manifestPath))
            {
                diagnostics.Add(new ArtDiagnostic(manifestPath, "$", "Missing pilot art kit manifest.json."));
                return new ArtKitValidationSummary(false, diagnostics, reports, materials, 0, 0, 0);
            }

            ValidateExportPreset(presetPath, diagnostics);

            using var manifestDoc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = manifestDoc.RootElement;
            if (!TryGetInt(root, "schema_version", out var schema) || schema != 1)
                diagnostics.Add(new ArtDiagnostic(manifestPath, "$.schema_version", "schema_version must be 1."));

            CheckForbiddenBrands(manifestPath, File.ReadAllText(manifestPath), diagnostics);

            if (root.TryGetProperty("atlas", out var atlasEl) && atlasEl.ValueKind == JsonValueKind.Object)
            {
                var relAtlas = TryGetString(atlasEl, "path");
                if (string.IsNullOrWhiteSpace(relAtlas))
                {
                    diagnostics.Add(new ArtDiagnostic(manifestPath, "$.atlas.path", "Atlas path is required."));
                }
                else
                {
                    var resolvedAtlas = ResolveSafeChildPath(rootFull, relAtlas, manifestPath, "$.atlas.path", diagnostics);
                    if (resolvedAtlas is not null)
                        ValidatePngDimensions(resolvedAtlas, "$.atlas.path", out atlasWidth, out atlasHeight, diagnostics);
                }
            }
            else
            {
                diagnostics.Add(new ArtDiagnostic(manifestPath, "$.atlas", "Atlas metadata object is required."));
            }

            if (root.TryGetProperty("materials", out var matsEl) && matsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in matsEl.EnumerateArray())
                {
                    var matId = item.ValueKind == JsonValueKind.String
                        ? item.GetString()
                        : TryGetString(item, "id");
                    if (!string.IsNullOrWhiteSpace(matId))
                        materials.Add(matId);
                }
            }
            foreach (var requiredMat in RequiredTestMaterials)
            {
                if (!materials.Contains(requiredMat, StringComparer.Ordinal))
                    diagnostics.Add(new ArtDiagnostic(manifestPath, "$.materials", $"Required test material '{requiredMat}' is missing."));
            }

            var seenModules = new HashSet<string>(StringComparer.Ordinal);
            if (!root.TryGetProperty("modules", out var modulesEl) || modulesEl.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add(new ArtDiagnostic(manifestPath, "$.modules", "modules array is required."));
            }
            else
            {
                var idx = 0;
                foreach (var modEl in modulesEl.EnumerateArray())
                {
                    ValidateModule(rootFull, manifestPath, idx, modEl, seenModules, reports, diagnostics);
                    idx++;
                }
            }

            foreach (var reqMod in RequiredModules)
            {
                if (!seenModules.Contains(reqMod))
                    diagnostics.Add(new ArtDiagnostic(manifestPath, "$.modules", $"Required pilot module '{reqMod}' is missing."));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or ArgumentException)
        {
            diagnostics.Add(new ArtDiagnostic(directory, "$", ex.Message));
        }

        var totalTris = reports.Sum(r => r.TriangleCount);
        return new ArtKitValidationSummary(
            diagnostics.Count == 0,
            Array.AsReadOnly(diagnostics.ToArray()),
            Array.AsReadOnly(reports.ToArray()),
            Array.AsReadOnly(materials.Distinct(StringComparer.Ordinal).ToArray()),
            totalTris,
            atlasWidth,
            atlasHeight);
    }

    private static void ValidateExportPreset(string presetPath, List<ArtDiagnostic> diagnostics)
    {
        if (!File.Exists(presetPath))
        {
            diagnostics.Add(new ArtDiagnostic(presetPath, "$", "Missing blender-gltf-export-preset.json."));
            return;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(presetPath));
        var root = doc.RootElement;
        if (!TryGetInt(root, "schema_version", out var sv) || sv != 1)
            diagnostics.Add(new ArtDiagnostic(presetPath, "$.schema_version", "Export preset schema_version must be 1."));
        if (!root.TryGetProperty("export_extras", out var extras) || extras.ValueKind != JsonValueKind.True)
            diagnostics.Add(new ArtDiagnostic(presetPath, "$.export_extras", "Blender preset must set export_extras=true to preserve semantic metadata."));
        if (!root.TryGetProperty("export_yup", out var yup) || yup.ValueKind != JsonValueKind.True)
            diagnostics.Add(new ArtDiagnostic(presetPath, "$.export_yup", "Blender preset must set export_yup=true for Godot glTF 2.0 import."));
    }

    private static void ValidateModule(
        string rootFull,
        string manifestPath,
        int index,
        JsonElement modEl,
        HashSet<string> seenModules,
        List<ArtModuleValidationReport> reports,
        List<ArtDiagnostic> diagnostics)
    {
        var fieldPrefix = $"$.modules[{index}]";
        var id = TryGetString(modEl, "id");
        var expectedClass = TryGetString(modEl, "semantic_class");
        var expectedMat = TryGetString(modEl, "material");
        var category = TryGetString(modEl, "category") ?? "urban";
        var relGltf = TryGetString(modEl, "gltf");

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(expectedClass) ||
            string.IsNullOrWhiteSpace(expectedMat) || string.IsNullOrWhiteSpace(relGltf))
        {
            diagnostics.Add(new ArtDiagnostic(manifestPath, fieldPrefix, "Module must define id, semantic_class, material, and gltf path."));
            return;
        }
        seenModules.Add(id);

        if (!CategoryBudgets.TryGetValue(category, out var budget))
        {
            diagnostics.Add(new ArtDiagnostic(manifestPath, $"{fieldPrefix}.category", $"Unsupported asset category '{category}'."));
            return;
        }

        var gltfPath = ResolveSafeChildPath(rootFull, relGltf, manifestPath, $"{fieldPrefix}.gltf", diagnostics);
        if (gltfPath is null || !File.Exists(gltfPath))
        {
            diagnostics.Add(new ArtDiagnostic(gltfPath ?? relGltf, "$", $"Missing glTF module file '{relGltf}'."));
            return;
        }

        var rawText = File.ReadAllText(gltfPath);
        CheckForbiddenBrands(gltfPath, rawText, diagnostics);
        using var gltfDoc = JsonDocument.Parse(rawText);
        var gltf = gltfDoc.RootElement;

        if (!gltf.TryGetProperty("asset", out var assetEl) || TryGetString(assetEl, "version") != "2.0")
            diagnostics.Add(new ArtDiagnostic(gltfPath, "$.asset.version", "glTF asset.version must be '2.0'."));

        // Verify semantic preservation inside glTF node extras (zero loss on export/re-import).
        if (!gltf.TryGetProperty("nodes", out var nodesEl) || nodesEl.ValueKind != JsonValueKind.Array || nodesEl.GetArrayLength() == 0)
        {
            diagnostics.Add(new ArtDiagnostic(gltfPath, "$.nodes", "glTF must declare at least one node with semantic extras."));
        }
        else
        {
            var node0 = nodesEl[0];
            if (!node0.TryGetProperty("extras", out var extrasEl) || extrasEl.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new ArtDiagnostic(gltfPath, "$.nodes[0].extras", "Node semantic extras were lost."));
            }
            else
            {
                var gltfId = TryGetString(extrasEl, "semantic_id");
                var gltfClass = TryGetString(extrasEl, "semantic_class");
                var gltfMaterial = TryGetString(extrasEl, "semantic_material");
                if (gltfId != id)
                    diagnostics.Add(new ArtDiagnostic(gltfPath, "$.nodes[0].extras.semantic_id", $"Expected semantic_id '{id}', found '{gltfId}'."));
                if (gltfClass != expectedClass)
                    diagnostics.Add(new ArtDiagnostic(gltfPath, "$.nodes[0].extras.semantic_class", $"Expected semantic_class '{expectedClass}', found '{gltfClass}'."));
                if (gltfMaterial != expectedMat)
                    diagnostics.Add(new ArtDiagnostic(gltfPath, "$.nodes[0].extras.semantic_material", $"Expected semantic_material '{expectedMat}', found '{gltfMaterial}'."));
                if (!extrasEl.TryGetProperty("collider", out var colEl) || colEl.ValueKind != JsonValueKind.True)
                    diagnostics.Add(new ArtDiagnostic(gltfPath, "$.nodes[0].extras.collider", "Module must declare collider=true metadata."));
            }
        }

        // Verify referenced images stay inside kit root and conform to 128..512 px.
        if (gltf.TryGetProperty("images", out var imagesEl) && imagesEl.ValueKind == JsonValueKind.Array)
        {
            var gltfDir = Path.GetDirectoryName(gltfPath)!;
            var imgIdx = 0;
            foreach (var imgEl in imagesEl.EnumerateArray())
            {
                var uri = TryGetString(imgEl, "uri");
                if (string.IsNullOrWhiteSpace(uri))
                {
                    diagnostics.Add(new ArtDiagnostic(gltfPath, $"$.images[{imgIdx}].uri", "Image uri is required."));
                }
                else if (!uri.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        diagnostics.Add(new ArtDiagnostic(gltfPath, $"$.images[{imgIdx}].uri", "External network URIs are forbidden."));
                    }
                    else
                    {
                        var candidate = Path.GetFullPath(Path.Combine(gltfDir, uri));
                        if (!IsUnderRoot(candidate, rootFull))
                            diagnostics.Add(new ArtDiagnostic(gltfPath, $"$.images[{imgIdx}].uri", "Texture path escapes kit root."));
                        else
                            ValidatePngDimensions(candidate, $"$.images[{imgIdx}].uri", out _, out _, diagnostics);
                    }
                }
                imgIdx++;
            }
        }

        // Verify buffers decode cleanly (embedded data:application/octet-stream;base64,... or safe relative .bin).
        if (gltf.TryGetProperty("buffers", out var buffersEl) && buffersEl.ValueKind == JsonValueKind.Array)
        {
            var bufIdx = 0;
            foreach (var bufEl in buffersEl.EnumerateArray())
            {
                var uri = TryGetString(bufEl, "uri");
                if (string.IsNullOrWhiteSpace(uri))
                {
                    diagnostics.Add(new ArtDiagnostic(gltfPath, $"$.buffers[{bufIdx}].uri", "Buffer URI missing."));
                    continue;
                }
                if (uri.StartsWith("data:", StringComparison.Ordinal))
                {
                    var comma = uri.IndexOf(',');
                    if (comma < 0)
                    {
                        diagnostics.Add(new ArtDiagnostic(gltfPath, $"$.buffers[{bufIdx}].uri", "Malformed data URI."));
                    }
                    else
                    {
                        var bytes = Convert.FromBase64String(uri[(comma + 1)..]);
                        if (TryGetInt(bufEl, "byteLength", out var declaredLen) && bytes.Length != declaredLen)
                            diagnostics.Add(new ArtDiagnostic(gltfPath, $"$.buffers[{bufIdx}].byteLength", $"Decoded byte length {bytes.Length} != declared {declaredLen}."));
                    }
                }
                bufIdx++;
            }
        }

        var triangles = ComputeTriangleCount(gltfPath, gltf, diagnostics);
        if (triangles < budget.MinTris || triangles > budget.MaxTris)
        {
            diagnostics.Add(new ArtDiagnostic(
                gltfPath,
                "$.meshes",
                $"Module '{id}' triangle count {triangles} violates {category} budget [{budget.MinTris}, {budget.MaxTris}]."));
        }

        reports.Add(new ArtModuleValidationReport(id, expectedClass, expectedMat, category, triangles, relGltf));
    }

    private static int ComputeTriangleCount(string gltfPath, JsonElement gltf, List<ArtDiagnostic> diagnostics)
    {
        if (!gltf.TryGetProperty("accessors", out var accessorsEl) || accessorsEl.ValueKind != JsonValueKind.Array ||
            !gltf.TryGetProperty("meshes", out var meshesEl) || meshesEl.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(new ArtDiagnostic(gltfPath, "$.meshes", "glTF must define accessors and meshes."));
            return 0;
        }

        var totalTris = 0;
        foreach (var mesh in meshesEl.EnumerateArray())
        {
            if (!mesh.TryGetProperty("primitives", out var primitives) || primitives.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var prim in primitives.EnumerateArray())
            {
                if (TryGetInt(prim, "indices", out var indicesIdx) && indicesIdx >= 0 && indicesIdx < accessorsEl.GetArrayLength())
                {
                    if (TryGetInt(accessorsEl[indicesIdx], "count", out var indexCount))
                    {
                        if (indexCount % 3 != 0)
                            diagnostics.Add(new ArtDiagnostic(gltfPath, "$.meshes", $"Triangle index count {indexCount} is not divisible by 3."));
                        totalTris += indexCount / 3;
                    }
                }
                else if (prim.TryGetProperty("attributes", out var attrs) && TryGetInt(attrs, "POSITION", out var posIdx) &&
                         posIdx >= 0 && posIdx < accessorsEl.GetArrayLength() && TryGetInt(accessorsEl[posIdx], "count", out var vertCount))
                {
                    totalTris += vertCount / 3;
                }
            }
        }
        return totalTris;
    }

    private static void ValidatePngDimensions(
        string pngPath,
        string field,
        out int width,
        out int height,
        List<ArtDiagnostic> diagnostics)
    {
        width = 0;
        height = 0;
        if (!File.Exists(pngPath) || IsSymlink(pngPath))
        {
            diagnostics.Add(new ArtDiagnostic(pngPath, field, "Referenced PNG texture is missing or a symlink."));
            return;
        }

        var bytes = File.ReadAllBytes(pngPath);
        ReadOnlySpan<byte> sig = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(sig) ||
            bytes[12] != (byte)'I' || bytes[13] != (byte)'H' || bytes[14] != (byte)'D' || bytes[15] != (byte)'R')
        {
            diagnostics.Add(new ArtDiagnostic(pngPath, field, "Texture is not a valid PNG with IHDR header."));
            return;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        if (width < MinTexturePixels || width > MaxTexturePixels || height < MinTexturePixels || height > MaxTexturePixels)
        {
            diagnostics.Add(new ArtDiagnostic(
                pngPath,
                field,
                $"Texture dimensions {width}x{height} violate budget [{MinTexturePixels}, {MaxTexturePixels}]."));
        }
    }

    private static void CheckForbiddenBrands(string path, string text, List<ArtDiagnostic> diagnostics)
    {
        var lower = text.ToLowerInvariant();
        foreach (var brand in ForbiddenRealBrands)
        {
            if (lower.Contains(brand, StringComparison.Ordinal))
                diagnostics.Add(new ArtDiagnostic(path, "$", $"Real-world brand name '{brand}' is forbidden."));
        }
    }

    private static string? ResolveSafeChildPath(
        string rootFull, string relative, string sourcePath, string field, List<ArtDiagnostic> diagnostics)
    {
        if (Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal))
        {
            diagnostics.Add(new ArtDiagnostic(sourcePath, field, $"Unsafe relative path '{relative}'."));
            return null;
        }
        var full = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!IsUnderRoot(full, rootFull))
        {
            diagnostics.Add(new ArtDiagnostic(sourcePath, field, $"Path '{relative}' escapes art kit root."));
            return null;
        }
        if (IsSymlink(full))
        {
            diagnostics.Add(new ArtDiagnostic(full, field, "Symbolic links are forbidden."));
            return null;
        }
        return full;
    }

    private static bool IsUnderRoot(string candidateFull, string rootFull)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootWithSep = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidateFull.StartsWith(rootWithSep, comparison);
    }

    private static bool IsSymlink(string path) =>
        (Directory.Exists(path) || File.Exists(path)) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    private static string? TryGetString(JsonElement obj, string prop) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetInt(JsonElement obj, string prop, out int value)
    {
        value = 0;
        return obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var el) &&
               el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value);
    }
}

