using System.Runtime.InteropServices;
using Gsa.Core.Mobility;
using Gsa.Engine.Diagnostics;
using Gsa.Engine.Localization;
using Gsa.Engine.Visuals;
using Gsa.Engine.World;
using Godot;

/// <summary>GTA III-styled main menu with live 3D São Datum backdrop and bilingual launch into Career, Sandbox, or Training.</summary>
public partial class Bootstrap : Control
{
    private bool _smokeTest;
    private int _frames;
    private readonly LocalizationService _localization = new();
    private Camera3D? _menuCamera;
    private float _menuOrbitAngle;
    private GsaPerfProbe? _perfProbe;

    public override void _Ready()
    {
        if (OS.GetCmdlineUserArgs().Contains("--city-save-smoke"))
        {
            SetProcess(false);
            Callable.From(() => OpenCity("career")).CallDeferred();
            return;
        }
        _perfProbe = GsaPerfProbe.TryCreateFromArgs(this, "bootstrap");
        _smokeTest = OS.GetCmdlineUserArgs().Contains("--smoke-test");
        var benchSmoke = OS.GetCmdlineUserArgs().Contains("--bench-smoke");
        if (benchSmoke)
        {
            SetProcess(false);
            Callable.From(OpenBench).CallDeferred();
            return;
        }
        _localization.Initialize();
        _localization.LocaleChanged += _ => RefreshText();
        GetNode<Button>("Center/Panel/Languages/Portuguese").Pressed += () => ChangeLanguage("pt-BR");
        GetNode<Button>("Center/Panel/Languages/English").Pressed += () => ChangeLanguage("en-US");
        GetNode<Button>("Center/Panel/OpenBench").Pressed += OpenBench;
        GetNode<Button>("Center/Panel/PlayCareer").Pressed += () => OpenCity("career");
        GetNode<Button>("Center/Panel/PlaySandbox").Pressed += () => OpenCity("sandbox");
        GetNode<Button>("Center/Panel/QuitButton").Pressed += () => GetTree().Quit();

        StyleMenuCard();
        if (!_smokeTest && !string.Equals(DisplayServer.GetName(), "headless", StringComparison.OrdinalIgnoreCase))
            Build3DMenuDiorama();

        RefreshText();
        if (_smokeTest)
        {
            try
            {
                LocalizationSmokeChecks.Run(_localization);
            }
            catch (Exception exception)
            {
                GD.PushError(exception.ToString());
                SetProcess(false);
                GetTree().Quit(1);
                return;
            }
        }
        GD.Print($"GSA_BOOTSTRAP_READY godot={Godot.Engine.GetVersionInfo()["string"]} " +
                 $"dotnet={System.Environment.Version} rid={RuntimeInformation.RuntimeIdentifier}");
    }

    private void StyleMenuCard()
    {
        var frame = GetNode<PanelContainer>("Center/PanelFrame");
        var panel = GetNode<VBoxContainer>("Center/Panel");
        frame.CustomMinimumSize = new Vector2(420f, 470f);
        frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.06f, 0.08f, 0.78f),
            BorderColor = new Color(0.95f, 0.76f, 0.24f, 0.85f),
            BorderWidthBottom = 2,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
        });
        panel.MouseFilter = Control.MouseFilterEnum.Pass;
    }

    private void Build3DMenuDiorama()
    {
        var vp = GetNode<SubViewport>("BackdropViewportContainer/BackdropViewport");
        var world = new Node3D { Name = "MenuDioramaWorld" };
        vp.AddChild(world);

        var skyMat = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.14f, 0.20f, 0.36f),
            SkyHorizonColor = new Color(0.88f, 0.48f, 0.28f),
            GroundHorizonColor = new Color(0.28f, 0.24f, 0.26f),
        };
        world.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky
                {
                    SkyMaterial = skyMat,
                    RadianceSize = Sky.RadianceSizeEnum.Size32,
                    ProcessMode = Sky.ProcessModeEnum.Incremental,
                },
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.56f, 0.38f, 0.36f),
                AmbientLightEnergy = 0.75f,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
                FogEnabled = true,
                FogMode = Godot.Environment.FogModeEnum.Depth,
                FogLightColor = new Color(0.62f, 0.44f, 0.38f),
                FogDepthBegin = 40f,
                FogDepthEnd = 190f,
            },
        });
        world.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-22f, -35f, 0f),
            LightColor = new Color(1.0f, 0.74f, 0.48f),
            LightEnergy = 1.3f,
        });
        var menuSunDir = Basis.FromEuler(new Vector3(Mathf.DegToRad(-22f), Mathf.DegToRad(-35f), 0f)).Z;
        GsaRetroShaders.UpdateAtmosphere(
            menuSunDir,
            new Color(1.0f, 0.78f, 0.56f), 1.45f,
            new Color(0.68f, 0.54f, 0.52f), 1.05f,
            new Color(0.62f, 0.44f, 0.38f), 40f, 190f);

        // Batched asphalt street & glowing city towers (world UVs, zero triplanar shader cost)
        var dioramaBatch = new GsaStaticMeshBatcher();
        var groundMat = GsaRetroTextures.CreateWorldMaterial("menu_asphalt_uv", "asphalt", new Color(0.85f, 0.88f, 0.92f), triplanar: false);
        dioramaBatch.AddBox(groundMat, new Vector3(0f, -0.1f, 0f), new Vector3(260f, 0.2f, 260f), uvScale: 0.14f);

        GsaRetroTextures.SetNightWindowEmissionEnergy(0.55f);
        var rng = new Random(2001);
        for (var i = 0; i < 28; i++)
        {
            var angle = i * (MathF.Tau / 28f);
            // Keep center boulevard corridor open toward the sunset horizon (-Z) and camera (+Z)
            var x = MathF.Cos(angle);
            var z = MathF.Sin(angle);
            if (MathF.Abs(x) < 0.34f)
                continue;
            var dist = 38f + (i % 3) * 18f;
            var h = 16f + (float)rng.NextDouble() * 34f;
            var w = 12f + (float)rng.NextDouble() * 8f;
            var style = (i % 3) switch
            {
                0 => "facade_commercial",
                1 => "facade_residential",
                _ => "facade_concrete",
            };
            var bMat = GsaRetroTextures.CreateWorldMaterial($"menu_bldg_{i % 3}_uv", style, Colors.White, triplanar: false, nightWindowGlow: true);
            dioramaBatch.AddBox(bMat, new Vector3(x * dist, h * 0.5f, z * dist), new Vector3(w, h, w), uvScale: 0.085f);
        }
        dioramaBatch.BuildSeparatedNodes(world, "MenuBackdrop", GeometryInstance3D.ShadowCastingSetting.Off);
        GsaRetroTextures.SetNightWindowEmissionEnergy(0.55f);

        var kombi = GsaMeshBuilder.BuildPlayerRig(LocomotionMode.Kombi);
        kombi.Root.Position = new Vector3(2.4f, 0f, -1.0f);
        kombi.Root.RotationDegrees = new Vector3(0f, 34f, 0f);
        world.AddChild(kombi.Root);

        var cart = GsaMeshBuilder.BuildPlayerRig(LocomotionMode.Handcart);
        cart.Root.Position = new Vector3(-2.3f, 0f, 1.2f);
        cart.Root.RotationDegrees = new Vector3(0f, -24f, 0f);
        world.AddChild(cart.Root);

        world.AddChild(new OmniLight3D
        {
            Name = "MenuStreetKeyLight",
            Position = new Vector3(0f, 5.2f, 2.8f),
            LightColor = new Color(1.0f, 0.88f, 0.68f),
            LightEnergy = 2.6f,
            OmniRange = 24f,
        });

        _menuCamera = new Camera3D { Position = new Vector3(0f, 3.0f, 9.2f), Fov = 54f, Current = true };
        world.AddChild(_menuCamera);
        _menuCamera.LookAt(new Vector3(0f, 2.2f, -14f), Vector3.Up);
    }

    private void ChangeLanguage(string locale)
    {
        var result = _localization.SetLocale(locale);
        if (result != Error.Ok)
            GetNode<Label>("Center/Panel/Status").Text = _localization.Translate("bootstrap.language_error");
    }

    private void OpenBench()
    {
        var result = GetTree().ChangeSceneToFile("res://world/TutorialBlock.tscn");
        if (result != Error.Ok)
        {
            GD.PushError($"Cannot open tutorial block: {result}");
            if (OS.GetCmdlineUserArgs().Contains("--bench-smoke"))
                GetTree().Quit(1);
        }
    }

    private void OpenCity(string mode)
    {
        GetTree().Root.SetMeta(SaoDatumCityScene.SandboxModeKey, mode);
        var result = GetTree().ChangeSceneToFile("res://world/SaoDatumCity.tscn");
        if (result != Error.Ok)
            GD.PushError($"Cannot open SaoDatumCity: {result}");
    }

    public override void _ExitTree() => _localization.Dispose();

    private void RefreshText()
    {
        GetNode<Label>("Center/Panel/Title").Text = _localization.Translate("bootstrap.title");
        GetNode<Label>("Center/Panel/Status").Text = _localization.Translate("bootstrap.status");
        GetNode<Label>("Center/Panel/Number").Text =
            _localization.Translate("bootstrap.coordinate_label") + ": " + _localization.FormatNumber(1234.56) + " m";
        GetNode<Label>("Center/Panel/Samples").Text = _localization.TranslatePlural("fixture.sample_count", 2);
        GetNode<Label>("Center/Panel/Language").Text = _localization.Translate("bootstrap.language");
        GetNode<Button>("Center/Panel/Languages/Portuguese").Text = _localization.Translate("language.pt_BR");
        GetNode<Button>("Center/Panel/Languages/English").Text = _localization.Translate("language.en_US");
        GetNode<Button>("Center/Panel/OpenBench").Text = _localization.Translate("bootstrap.open_bench");
        GetNode<Button>("Center/Panel/PlayCareer").Text = _localization.Translate("bootstrap.play_career");
        GetNode<Button>("Center/Panel/PlaySandbox").Text = _localization.Translate("bootstrap.play_sandbox");
        GetNode<Button>("Center/Panel/QuitButton").Text = _localization.Translate("bootstrap.quit");
    }

    public override void _Process(double delta)
    {
        if (_menuCamera is not null)
        {
            _menuOrbitAngle += (float)delta * 0.18f;
            var r = 10.5f;
            _menuCamera.Position = new Vector3(MathF.Sin(_menuOrbitAngle) * r, 3.8f + MathF.Sin(_menuOrbitAngle * 0.5f) * 0.8f, MathF.Cos(_menuOrbitAngle) * r);
            _menuCamera.LookAt(new Vector3(0f, 1.8f, 0f), Vector3.Up);
        }
        if (_smokeTest && ++_frames == 3)
        {
            GD.Print("GSA_BOOTSTRAP_SMOKE_OK");
            GetTree().Quit();
        }
        else if (!_smokeTest)
        {
            GsaScreenshotHelper.TryCaptureOnFrame(this, ++_frames, targetFrame: 12);
        }
        _perfProbe?.StepFrame(this);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
            GetTree().Quit();
    }
}
