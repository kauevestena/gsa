using Godot;

namespace Gsa.Engine.Visuals;

/// <summary>Lightweight PS2-era Gouraud vertex-lit shaders for 60+ FPS at 1080p on integrated GPUs.</summary>
public static class GsaRetroShaders
{
    private const string SolidShaderCode = """
        shader_type spatial;
        render_mode unshaded, fog_disabled;

        uniform vec4 albedo_color : source_color = vec4(1.0);
        uniform vec3 emission_linear = vec3(0.0);
        uniform vec3 sun_dir = vec3(0.42, 0.67, 0.61);
        uniform vec3 sun_linear = vec3(1.10, 0.96, 0.72);
        uniform vec3 ambient_linear = vec3(0.16, 0.20, 0.25);
        uniform vec3 fog_color : source_color = vec3(0.64, 0.72, 0.78);
        uniform float fog_begin = 75.0;
        uniform float fog_end = 390.0;

        varying vec3 v_lit;
        varying vec3 v_view_pos;

        void vertex() {
            vec3 wn = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
            float ndl = max(dot(wn, sun_dir), 0.0);
            vec3 light = ambient_linear + sun_linear * ndl;
            vec3 vc = COLOR.rgb * (COLOR.rgb * (COLOR.rgb * 0.3053 + 0.6821) + 0.0125);
            v_lit = albedo_color.rgb * vc * light + emission_linear;
            v_view_pos = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        void fragment() {
            float fog = clamp((length(v_view_pos) - fog_begin) / max(1.0, fog_end - fog_begin), 0.0, 1.0);
            ALBEDO = mix(v_lit, fog_color, fog);
        }
        """;

    private const string TexturedShaderCode = """
        shader_type spatial;
        render_mode unshaded, fog_disabled;

        uniform sampler2D albedo_tex : source_color, filter_nearest_mipmap, repeat_enable;
        uniform vec4 albedo_color : source_color = vec4(1.0);
        uniform vec3 sun_dir = vec3(0.42, 0.67, 0.61);
        uniform vec3 sun_linear = vec3(1.10, 0.96, 0.72);
        uniform vec3 ambient_linear = vec3(0.16, 0.20, 0.25);
        uniform vec3 fog_color : source_color = vec3(0.64, 0.72, 0.78);
        uniform float fog_begin = 75.0;
        uniform float fog_end = 390.0;
        uniform vec2 uv_scale = vec2(1.0);

        varying vec3 v_tint_lit;
        varying vec3 v_view_pos;
        varying vec2 v_uv;

        void vertex() {
            vec3 wn = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
            float ndl = max(dot(wn, sun_dir), 0.0);
            v_tint_lit = albedo_color.rgb * COLOR.rgb * (ambient_linear + sun_linear * ndl);
            v_view_pos = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
            v_uv = UV * uv_scale;
        }

        void fragment() {
            vec3 tex = texture(albedo_tex, v_uv).rgb;
            float fog = clamp((length(v_view_pos) - fog_begin) / max(1.0, fog_end - fog_begin), 0.0, 1.0);
            ALBEDO = mix(tex * v_tint_lit, fog_color, fog);
        }
        """;

    private const string BuildingNightShaderCode = """
        shader_type spatial;
        render_mode unshaded, fog_disabled;

        uniform sampler2D albedo_tex : source_color, filter_nearest_mipmap, repeat_enable;
        uniform sampler2D emission_tex : source_color, filter_nearest_mipmap, repeat_enable;
        uniform vec4 albedo_color : source_color = vec4(1.0);
        uniform vec3 sun_dir = vec3(0.42, 0.67, 0.61);
        uniform vec3 sun_linear = vec3(1.10, 0.96, 0.72);
        uniform vec3 ambient_linear = vec3(0.16, 0.20, 0.25);
        uniform vec3 fog_color : source_color = vec3(0.64, 0.72, 0.78);
        uniform float fog_begin = 75.0;
        uniform float fog_end = 390.0;
        uniform vec2 uv_scale = vec2(1.0);
        uniform vec3 night_emission_linear = vec3(0.0);

        varying vec3 v_tint_lit;
        varying vec3 v_view_pos;
        varying vec2 v_uv;

        void vertex() {
            vec3 wn = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
            float ndl = max(dot(wn, sun_dir), 0.0);
            v_tint_lit = albedo_color.rgb * COLOR.rgb * (ambient_linear + sun_linear * ndl);
            v_view_pos = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
            v_uv = UV * uv_scale;
        }

        void fragment() {
            vec3 base = texture(albedo_tex, v_uv).rgb * v_tint_lit;
            vec3 glow = texture(emission_tex, v_uv).rgb * night_emission_linear;
            float fog = clamp((length(v_view_pos) - fog_begin) / max(1.0, fog_end - fog_begin), 0.0, 1.0);
            ALBEDO = mix(base + glow, fog_color, fog);
        }
        """;

    private static Shader? _solidShader;
    private static Shader? _texturedShader;
    private static Shader? _buildingNightShader;
    private static readonly List<ShaderMaterial> TrackedMaterials = [];
    private static readonly List<ShaderMaterial> NightBuildingMaterials = [];

    private static Vector3 _sunDir = new(0.42f, 0.67f, 0.61f);
    private static Vector3 _sunLinear = new(1.0f, 0.88f, 0.68f);
    private static Vector3 _ambientLinear = new(0.22f, 0.26f, 0.30f);
    private static Color _fogColor = new(0.64f, 0.72f, 0.78f);
    private static float _fogBegin = 75f;
    private static float _fogEnd = 390f;
    private static float _nightWindowEnergy;

    private static Shader SolidShader => _solidShader ??= new Shader { Code = SolidShaderCode };
    private static Shader TexturedShader => _texturedShader ??= new Shader { Code = TexturedShaderCode };
    private static Shader BuildingNightShader => _buildingNightShader ??= new Shader { Code = BuildingNightShaderCode };

    public static ShaderMaterial CreateSolidMaterial(Color albedoColor, bool emit = false, float emissionEnergy = 1.2f)
    {
        var mat = new ShaderMaterial { Shader = SolidShader };
        mat.SetShaderParameter("albedo_color", albedoColor);
        if (emit)
        {
            var lin = albedoColor.SrgbToLinear();
            mat.SetShaderParameter("emission_linear", new Vector3(lin.R * emissionEnergy, lin.G * emissionEnergy, lin.B * emissionEnergy));
        }
        ApplyAtmosphereUniforms(mat);
        TrackedMaterials.Add(mat);
        return mat;
    }

    public static ShaderMaterial CreateTexturedMaterial(
        Texture2D albedoTex,
        Color tint,
        Vector2 uvScale,
        Texture2D? nightEmissionTex = null)
    {
        var hasNight = nightEmissionTex is not null;
        var mat = new ShaderMaterial { Shader = hasNight ? BuildingNightShader : TexturedShader };
        mat.SetShaderParameter("albedo_tex", albedoTex);
        mat.SetShaderParameter("albedo_color", tint);
        mat.SetShaderParameter("uv_scale", uvScale);
        if (hasNight)
        {
            mat.SetShaderParameter("emission_tex", nightEmissionTex!);
            NightBuildingMaterials.Add(mat);
            ApplyNightWindowUniform(mat);
        }
        ApplyAtmosphereUniforms(mat);
        TrackedMaterials.Add(mat);
        return mat;
    }

    public static void UpdateAtmosphere(
        Vector3 sunDir,
        Color sunColor,
        float sunEnergy,
        Color ambientColor,
        float ambientEnergy,
        Color fogColor,
        float fogBegin,
        float fogEnd)
    {
        _sunDir = sunDir.Normalized();
        var sLin = sunColor.SrgbToLinear();
        _sunLinear = new Vector3(sLin.R * sunEnergy, sLin.G * sunEnergy, sLin.B * sunEnergy);
        var aLin = ambientColor.SrgbToLinear();
        _ambientLinear = new Vector3(aLin.R * ambientEnergy, aLin.G * ambientEnergy, aLin.B * ambientEnergy);
        _fogColor = fogColor;
        _fogBegin = fogBegin;
        _fogEnd = fogEnd;

        for (var i = 0; i < TrackedMaterials.Count; i++)
            ApplyAtmosphereUniforms(TrackedMaterials[i]);
    }

    public static void SetNightWindowEmissionEnergy(float energy)
    {
        if (MathF.Abs(_nightWindowEnergy - energy) < 0.005f && NightBuildingMaterials.Count == 0)
            return;
        _nightWindowEnergy = energy;
        for (var i = 0; i < NightBuildingMaterials.Count; i++)
            ApplyNightWindowUniform(NightBuildingMaterials[i]);
    }

    private static void ApplyAtmosphereUniforms(ShaderMaterial mat)
    {
        mat.SetShaderParameter("sun_dir", _sunDir);
        mat.SetShaderParameter("sun_linear", _sunLinear);
        mat.SetShaderParameter("ambient_linear", _ambientLinear);
        mat.SetShaderParameter("fog_color", _fogColor);
        mat.SetShaderParameter("fog_begin", _fogBegin);
        mat.SetShaderParameter("fog_end", _fogEnd);
    }

    private static void ApplyNightWindowUniform(ShaderMaterial mat)
    {
        var glow = new Color(1.0f, 0.82f, 0.45f).SrgbToLinear();
        mat.SetShaderParameter(
            "night_emission_linear",
            new Vector3(glow.R * _nightWindowEnergy, glow.G * _nightWindowEnergy, glow.B * _nightWindowEnergy));
    }
}
