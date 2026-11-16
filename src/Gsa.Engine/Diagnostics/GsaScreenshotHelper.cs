using Godot;

namespace Gsa.Engine.Diagnostics;

/// <summary>Optional CLI screenshot helper (--capture-screenshot=&lt;path&gt;) for visual verification.</summary>
public static class GsaScreenshotHelper
{
    public static string? GetRequestedPath()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--capture-screenshot=", StringComparison.Ordinal))
                return arg["--capture-screenshot=".Length..];
        }
        return null;
    }

    public static bool TryCaptureOnFrame(Node caller, int frameCount, int targetFrame = 20)
    {
        var path = GetRequestedPath();
        if (string.IsNullOrWhiteSpace(path) || frameCount < targetFrame)
            return false;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var image = caller.GetViewport().GetTexture().GetImage();
        var err = image.SavePng(path);
        GD.Print($"GSA_SCREENSHOT_SAVED path={path} status={err}");
        caller.GetTree().Quit(err == Error.Ok ? 0 : 1);
        return true;
    }
}

