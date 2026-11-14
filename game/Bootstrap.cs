using System.Runtime.InteropServices;
using Gsa.Engine.Localization;
using Godot;

/// <summary>Verifies the managed runtime before introducing gameplay modules.</summary>
public partial class Bootstrap : Control
{
    private bool _smokeTest;
    private int _frames;
    private readonly LocalizationService _localization = new();

    public override void _Ready()
    {
        _smokeTest = OS.GetCmdlineUserArgs().Contains("--smoke-test");
        if (OS.GetCmdlineUserArgs().Contains("--bench-smoke"))
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
    }

    public override void _Process(double delta)
    {
        if (_smokeTest && ++_frames == 3)
        {
            GD.Print("GSA_BOOTSTRAP_SMOKE_OK");
            GetTree().Quit();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
            GetTree().Quit();
    }
}
