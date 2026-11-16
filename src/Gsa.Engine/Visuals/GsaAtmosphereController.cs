using Gsa.Core.Traffic;
using Godot;

namespace Gsa.Engine.Visuals;

/// <summary>Drives GTA III sky, sun elevation, 300-600 m atmospheric fog, night lights, rain streaks, and optional PS2 dither.</summary>
public sealed class GsaAtmosphereController
{
    private readonly Node3D _root;
    private readonly WorldEnvironment _worldEnv;
    private readonly Godot.Environment _env;
    private readonly ProceduralSkyMaterial _skyMat;
    private readonly DirectionalLight3D _sun;
    private readonly MultiMeshInstance3D _rainMesh;
    private readonly Vector3[] _rainOffsets;
    private readonly List<(StandardMaterial3D BulbMat, OmniLight3D? Light)> _streetlamps = [];
    private CanvasLayer? _ditherLayer;
    private ShaderMaterial? _filterMat;
    private bool _lastRaining;
    private bool _ditherEnabled;
    private bool _daguerreotypeEnabled;

    public bool RetroDitherEnabled => _ditherEnabled;
    public bool DaguerreotypeEnabled => _daguerreotypeEnabled;

    public GsaAtmosphereController(Node3D parent, bool enableRetroDither = false)
    {
        _root = new Node3D { Name = "Atmosphere" };
        parent.AddChild(_root);

        _skyMat = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.24f, 0.46f, 0.72f),
            SkyHorizonColor = new Color(0.66f, 0.74f, 0.80f),
            GroundBottomColor = new Color(0.14f, 0.16f, 0.18f),
            GroundHorizonColor = new Color(0.55f, 0.62f, 0.68f),
            SunAngleMax = 14f,
            SunCurve = 0.12f,
            SkyCurve = 0.22f,
        };
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _skyMat },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 0.65f,
            AmbientLightEnergy = 0.75f,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogLightColor = new Color(0.62f, 0.68f, 0.74f),
            FogLightEnergy = 1.0f,
            FogDepthBegin = 75f,
            FogDepthEnd = 390f,
            FogSkyAffect = 0.35f,
            GlowEnabled = true,
            GlowIntensity = 0.32f,
            GlowStrength = 0.85f,
            GlowBloom = 0.14f,
            GlowHdrThreshold = 0.92f,
        };
        _worldEnv = new WorldEnvironment { Name = "Environment", Environment = _env };
        _root.AddChild(_worldEnv);

        _sun = new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-42f, -35f, 0f),
            LightColor = new Color(1.0f, 0.94f, 0.82f),
            LightEnergy = 1.25f,
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 120f,
        };
        _root.AddChild(_sun);

        // Lightweight gl_compatibility-safe rain streaks MultiMesh
        const int dropCount = 220;
        _rainOffsets = new Vector3[dropCount];
        var rng = new Random(200110);
        for (var i = 0; i < dropCount; i++)
        {
            _rainOffsets[i] = new Vector3(
                (float)(rng.NextDouble() * 44.0 - 22.0),
                (float)(rng.NextDouble() * 20.0),
                (float)(rng.NextDouble() * 44.0 - 22.0));
        }

        var dropMesh = new BoxMesh
        {
            Size = new Vector3(0.03f, 0.85f, 0.03f),
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.75f, 0.86f, 0.96f, 0.45f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = dropMesh,
            InstanceCount = dropCount,
        };
        _rainMesh = new MultiMeshInstance3D
        {
            Name = "RainStreaks",
            Multimesh = mm,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _root.AddChild(_rainMesh);

        BuildRetroDitherOverlay(parent, enableRetroDither);
    }

    public void RegisterStreetlamp(StandardMaterial3D bulbMat, OmniLight3D? light) =>
        _streetlamps.Add((bulbMat, light));

    public void ClearStreetlamps() => _streetlamps.Clear();

    public void SetRetroDitherEnabled(bool enabled)
    {
        _ditherEnabled = enabled;
        SyncFilterLayer();
    }

    public void SetDaguerreotypeEnabled(bool enabled)
    {
        _daguerreotypeEnabled = enabled;
        SyncFilterLayer();
    }

    private void SyncFilterLayer()
    {
        if (_ditherLayer is null)
            return;
        _ditherLayer.Visible = _ditherEnabled || _daguerreotypeEnabled;
        if (_filterMat is not null)
        {
            _filterMat.SetShaderParameter("dither_strength", _ditherEnabled ? 0.65f : 0.0f);
            _filterMat.SetShaderParameter("sepia_amount", _daguerreotypeEnabled ? 0.88f : 0.0f);
        }
    }

    public void Update(WeatherAndTimeSnapshot weather, Vector3 focusGodotPos, double dtSeconds)
    {
        var hour = weather.HourOfDay;
        var isNight = weather.IsNight;
        var isRaining = weather.IsRaining;

        // Smooth GTA III time-of-day sky & fog palette (morning haze, crisp midday, golden dusk, deep indigo night)
        Color skyTop;
        Color skyHorizon;
        Color sunColor;
        Color fogColor;
        float sunEnergy;
        float ambientEnergy;
        float sunPitchDeg;

        if (isNight)
        {
            skyTop = new Color(0.05f, 0.08f, 0.16f);
            skyHorizon = new Color(0.12f, 0.16f, 0.25f);
            sunColor = new Color(0.45f, 0.56f, 0.78f);
            fogColor = new Color(0.09f, 0.12f, 0.18f);
            sunEnergy = 0.24f;
            ambientEnergy = 0.42f;
            sunPitchDeg = -55f;
        }
        else if (hour < 8.5)
        {
            var t = (float)((hour - 6.0) / 2.5);
            skyTop = new Color(0.18f, 0.34f, 0.56f).Lerp(new Color(0.24f, 0.48f, 0.76f), t);
            skyHorizon = new Color(0.84f, 0.62f, 0.44f).Lerp(new Color(0.68f, 0.76f, 0.82f), t);
            sunColor = new Color(1.0f, 0.76f, 0.50f).Lerp(new Color(1.0f, 0.94f, 0.82f), t);
            fogColor = new Color(0.72f, 0.66f, 0.60f).Lerp(new Color(0.64f, 0.72f, 0.78f), t);
            sunEnergy = 0.75f + 0.55f * t;
            ambientEnergy = 0.60f + 0.18f * t;
            sunPitchDeg = -18f - 28f * t;
        }
        else if (hour >= 17.0)
        {
            var t = (float)Math.Clamp((hour - 17.0) / 3.0, 0.0, 1.0);
            skyTop = new Color(0.24f, 0.48f, 0.76f).Lerp(new Color(0.16f, 0.18f, 0.34f), t);
            skyHorizon = new Color(0.68f, 0.76f, 0.82f).Lerp(new Color(0.86f, 0.45f, 0.26f), t);
            sunColor = new Color(1.0f, 0.94f, 0.82f).Lerp(new Color(1.0f, 0.52f, 0.26f), t);
            fogColor = new Color(0.64f, 0.72f, 0.78f).Lerp(new Color(0.55f, 0.40f, 0.36f), t);
            sunEnergy = 1.25f - 0.70f * t;
            ambientEnergy = 0.75f - 0.25f * t;
            sunPitchDeg = -46f + 30f * t;
        }
        else
        {
            skyTop = new Color(0.24f, 0.48f, 0.76f);
            skyHorizon = new Color(0.68f, 0.76f, 0.82f);
            sunColor = new Color(1.0f, 0.95f, 0.85f);
            fogColor = new Color(0.64f, 0.72f, 0.78f);
            sunEnergy = 1.28f;
            ambientEnergy = 0.78f;
            sunPitchDeg = -52f;
        }

        if (isRaining)
        {
            var rainDesat = new Color(0.36f, 0.41f, 0.46f);
            skyTop = skyTop.Lerp(rainDesat * 0.75f, 0.65f);
            skyHorizon = skyHorizon.Lerp(rainDesat, 0.72f);
            fogColor = fogColor.Lerp(rainDesat, 0.78f);
            sunEnergy *= 0.52f;
            _env.FogDepthBegin = 45f;
            _env.FogDepthEnd = 250f;
        }
        else
        {
            _env.FogDepthBegin = 75f;
            _env.FogDepthEnd = 390f;
        }

        _skyMat.SkyTopColor = skyTop;
        _skyMat.SkyHorizonColor = skyHorizon;
        _skyMat.GroundHorizonColor = fogColor;
        _env.FogLightColor = fogColor;
        _env.AmbientLightEnergy = ambientEnergy;
        _sun.LightColor = sunColor;
        _sun.LightEnergy = sunEnergy;
        _sun.RotationDegrees = new Vector3(sunPitchDeg, -35f + (float)((hour - 6.0) * 6.0), 0f);

        GsaRetroTextures.SetNightWindowEmissionEnergy(isNight ? 0.55f : 0.0f);
        if (isRaining != _lastRaining)
        {
            _lastRaining = isRaining;
            GsaRetroTextures.SetWetAsphaltState(isRaining);
        }

        var lampEnergy = isNight ? 2.4f : 0.0f;
        foreach (var (bulbMat, light) in _streetlamps)
        {
            bulbMat.EmissionEnergyMultiplier = lampEnergy;
            if (light is not null)
                light.Visible = isNight;
        }

        _rainMesh.Visible = isRaining;
        if (isRaining && dtSeconds > 0.0)
        {
            var dropBasis = Basis.FromEuler(new Vector3(0.10f, 0f, 0.06f));
            var fall = (float)(dtSeconds * 21.0);
            var mm = _rainMesh.Multimesh;
            for (var i = 0; i < _rainOffsets.Length; i++)
            {
                var off = _rainOffsets[i];
                off.Y -= fall;
                if (off.Y < -1.0f)
                    off.Y += 20.0f;
                _rainOffsets[i] = off;
                var pos = new Vector3(focusGodotPos.X + off.X, focusGodotPos.Y + off.Y, focusGodotPos.Z + off.Z);
                mm.SetInstanceTransform(i, new Transform3D(dropBasis, pos));
            }
        }
    }

    private void BuildRetroDitherOverlay(Node parent, bool enabled)
    {
        if (!ResourceLoader.Exists("res://shaders/retro_dither.gdshader"))
            return;
        var shader = ResourceLoader.Load<Shader>("res://shaders/retro_dither.gdshader");
        if (shader is null)
            return;

        _ditherEnabled = enabled;
        _filterMat = new ShaderMaterial { Shader = shader };
        _ditherLayer = new CanvasLayer { Name = "RetroDitherLayer", Layer = 2, Visible = enabled };
        var rect = new ColorRect
        {
            AnchorRight = 1f,
            AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = _filterMat,
        };
        _ditherLayer.AddChild(rect);
        parent.AddChild(_ditherLayer);
        SyncFilterLayer();
    }
}

