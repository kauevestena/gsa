using Godot;

namespace Gsa.Engine.Visuals;

/// <summary>Procedural PS2-era (128-256 px) urban textures and cached materials for GTA III styling.</summary>
public static class GsaRetroTextures
{
    private static readonly Dictionary<string, ImageTexture> TextureCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, StandardMaterial3D> MaterialCache = new(StringComparer.Ordinal);

    public static ImageTexture GetTexture(string kind)
    {
        if (TextureCache.TryGetValue(kind, out var existing))
            return existing;

        var tex = kind switch
        {
            "asphalt" => BuildAsphaltTexture(),
            "sidewalk" => BuildSidewalkTexture(),
            "crosswalk" => BuildCrosswalkTexture(),
            "facade_commercial" => BuildBuildingFacadeTexture(
                wall: new Color(0.68f, 0.44f, 0.34f),
                trim: new Color(0.46f, 0.35f, 0.28f),
                window: new Color(0.14f, 0.21f, 0.27f),
                shopAwning: new Color(0.76f, 0.22f, 0.17f)),
            "facade_residential" => BuildBuildingFacadeTexture(
                wall: new Color(0.76f, 0.70f, 0.56f),
                trim: new Color(0.56f, 0.52f, 0.44f),
                window: new Color(0.16f, 0.24f, 0.29f),
                shopAwning: new Color(0.17f, 0.47f, 0.38f)),
            "facade_concrete" => BuildBuildingFacadeTexture(
                wall: new Color(0.56f, 0.58f, 0.56f),
                trim: new Color(0.38f, 0.40f, 0.40f),
                window: new Color(0.16f, 0.22f, 0.26f),
                shopAwning: new Color(0.22f, 0.36f, 0.56f)),
            "facade_night_windows" => BuildNightWindowMaskTexture(),
            "warehouse" => BuildCorrugatedTexture(new Color(0.42f, 0.46f, 0.49f)),
            "glass_tower" => BuildGlassCurtainwallTexture(),
            "park_grass" => BuildNoiseTileTexture(128, new Color(0.19f, 0.40f, 0.18f), 0.06f, seed: 41),
            "dirt_road" => BuildNoiseTileTexture(128, new Color(0.48f, 0.31f, 0.20f), 0.08f, seed: 83),
            "water" => BuildWaterTexture(),
            "shadow_blob" => BuildShadowBlobTexture(),
            _ => BuildNoiseTileTexture(128, new Color(0.55f, 0.55f, 0.55f), 0.04f, seed: 11),
        };
        TextureCache[kind] = tex;
        return tex;
    }

    public static StandardMaterial3D CreateWorldMaterial(
        string key,
        string textureKind,
        Color tint,
        Vector3? uvScale = null,
        float roughness = 0.88f,
        bool triplanar = false,
        bool unshaded = false,
        bool transparent = false,
        bool nightWindowGlow = false)
    {
        if (MaterialCache.TryGetValue(key, out var cached))
            return cached;

        var mat = new StandardMaterial3D
        {
            AlbedoTexture = GetTexture(textureKind),
            AlbedoColor = tint,
            Roughness = roughness,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
            Uv1Triplanar = triplanar,
            Uv1WorldTriplanar = triplanar,
        };
        if (uvScale.HasValue)
            mat.Uv1Scale = uvScale.Value;
        if (unshaded)
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        if (transparent)
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        if (nightWindowGlow)
        {
            mat.EmissionEnabled = true;
            mat.EmissionTexture = GetTexture("facade_night_windows");
            mat.Emission = new Color(1.0f, 0.82f, 0.45f);
            mat.EmissionEnergyMultiplier = 0.0f; // Driven dynamically by GsaAtmosphereController at dusk/night!
        }

        MaterialCache[key] = mat;
        return mat;
    }

    public static void SetNightWindowEmissionEnergy(float energy)
    {
        foreach (var (key, mat) in MaterialCache)
        {
            if (mat.EmissionEnabled && (key.StartsWith("bldg_", StringComparison.Ordinal) || key.StartsWith("menu_bldg_", StringComparison.Ordinal)))
                mat.EmissionEnergyMultiplier = energy;
        }
    }

    public static void SetWetAsphaltState(bool isRaining)
    {
        foreach (var (key, mat) in MaterialCache)
        {
            if (key.Contains("asphalt", StringComparison.Ordinal) || key.Contains("road", StringComparison.Ordinal))
            {
                mat.Roughness = isRaining ? 0.28f : 0.88f;
                mat.AlbedoColor = isRaining ? new Color(0.68f, 0.72f, 0.78f) : Colors.White;
            }
        }
    }

    private static ImageTexture BuildAsphaltTexture()
    {
        const int size = 256;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        var rng = new Random(1901);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var grain = (float)(rng.NextDouble() - 0.5) * 0.045f;
                var streak = MathF.Sin(x * 0.08f) * 0.01f;
                var v = Math.Clamp(0.16f + grain + streak, 0.10f, 0.24f);
                img.SetPixel(x, y, new Color(v * 0.96f, v * 0.98f, v * 1.04f));
            }
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture BuildSidewalkTexture()
    {
        const int size = 256;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        var rng = new Random(2001);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var seam = (x % 64 < 3) || (y % 64 < 3);
                var speck = (float)(rng.NextDouble() - 0.5) * 0.04f;
                var baseVal = seam ? 0.36f : 0.58f + speck;
                img.SetPixel(x, y, new Color(baseVal * 1.01f, baseVal * 0.99f, baseVal * 0.95f));
            }
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture BuildCrosswalkTexture()
    {
        const int size = 128;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var stripe = (x % 32) < 16 && y >= 12 && y < size - 12;
                img.SetPixel(x, y, stripe ? new Color(0.84f, 0.84f, 0.80f) : new Color(0.16f, 0.17f, 0.19f));
            }
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture BuildBuildingFacadeTexture(Color wall, Color trim, Color window, Color shopAwning)
    {
        const int size = 256;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        var rng = new Random(unchecked((int)wall.ToArgb32()));
        _ = shopAwning;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var noise = (float)(rng.NextDouble() - 0.5) * 0.032f;
                var brickSeam = (y % 16 < 2 || ((x + (y / 16) * 12) % 24 < 1)) ? -0.025f : 0f;
                var c = new Color(
                    Math.Clamp(wall.R + noise + brickSeam, 0f, 1f),
                    Math.Clamp(wall.G + noise + brickSeam, 0f, 1f),
                    Math.Clamp(wall.B + noise + brickSeam, 0f, 1f));

                // Clean architectural floor cornices & vertical piers (3 uniform stories per tile)
                var row = y % 85;
                var col = x % 64;
                if (row < 7 || row >= 80)
                {
                    c = trim * (row < 4 ? 1.12f : 0.88f);
                }
                else if (col is >= 14 and < 50 && row is >= 20 and < 68)
                {
                    var isSill = row >= 63;
                    var isLintel = row < 24;
                    var isFrame = col < 17 || col >= 47 || Math.Abs(col - 32) <= 1 || Math.Abs(row - 43) <= 1;
                    c = isSill || isLintel ? trim * 1.15f : (isFrame ? trim * 0.92f : window);
                }
                img.SetPixel(x, y, c);
            }
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture BuildNightWindowMaskTexture()
    {
        const int size = 256;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        var rng = new Random(7741);
        var litRooms = new bool[4, 3];
        for (var ry = 0; ry < 3; ry++)
            for (var rx = 0; rx < 4; rx++)
                litRooms[rx, ry] = rng.NextDouble() < 0.30;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var glow = Colors.Black;
                var rx = Math.Clamp(x / 64, 0, 3);
                var ry = Math.Clamp(y / 85, 0, 2);
                var row = y % 85;
                var col = x % 64;
                if (litRooms[rx, ry] && col is >= 18 and < 46 && row is >= 25 and < 62 &&
                    Math.Abs(col - 32) > 1 && Math.Abs(row - 43) > 1)
                {
                    var warm = ((rx + ry) % 2 == 0)
                        ? new Color(0.95f, 0.78f, 0.40f)
                        : new Color(0.86f, 0.84f, 0.62f);
                    glow = warm;
                }
                img.SetPixel(x, y, glow);
            }
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture BuildCorrugatedTexture(Color baseColor)
    {
        const int size = 128;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var rib = MathF.Sin(x * (MathF.PI * 2f / 8f)) * 0.08f;
                var band = (y % 64 < 3) ? -0.10f : 0f;
                var c = new Color(
                    Math.Clamp(baseColor.R + rib + band, 0f, 1f),
                    Math.Clamp(baseColor.G + rib + band, 0f, 1f),
                    Math.Clamp(baseColor.B + rib + band, 0f, 1f));
                img.SetPixel(x, y, c);
            }
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture BuildGlassCurtainwallTexture()
    {
        const int size = 256;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var mullion = (x % 32 < 3) || (y % 32 < 4);
                var sheen = MathF.Sin((x + y) * 0.05f) * 0.04f;
                var c = mullion
                    ? new Color(0.24f, 0.28f, 0.33f)
                    : new Color(0.20f + sheen, 0.44f + sheen, 0.56f + sheen);
                img.SetPixel(x, y, c);
            }
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture BuildWaterTexture()
    {
        const int size = 128;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var wave = MathF.Sin(x * 0.15f + MathF.Cos(y * 0.12f) * 2.0f) * 0.05f;
                img.SetPixel(x, y, new Color(0.08f + wave * 0.5f, 0.33f + wave, 0.52f + wave));
            }
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture BuildNoiseTileTexture(int size, Color baseColor, float variance, int seed)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        var rng = new Random(seed);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var delta = (float)(rng.NextDouble() - 0.5) * variance * 2f;
                img.SetPixel(x, y, new Color(
                    Math.Clamp(baseColor.R + delta, 0f, 1f),
                    Math.Clamp(baseColor.G + delta, 0f, 1f),
                    Math.Clamp(baseColor.B + delta, 0f, 1f)));
            }
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture BuildShadowBlobTexture()
    {
        const int size = 64;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var half = (size - 1) * 0.5f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = (x - half) / half;
                var dy = (y - half) / half;
                var d = MathF.Sqrt(dx * dx + dy * dy);
                var alpha = Math.Clamp(1.0f - d, 0f, 1f);
                alpha = alpha * alpha * 0.55f;
                img.SetPixel(x, y, new Color(0.02f, 0.02f, 0.03f, alpha));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }
}
