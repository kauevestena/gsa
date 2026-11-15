using System.Text.Json;
using Gsa.Tools;
using Xunit;

namespace Gsa.Tools.Tests;

public sealed class ArtKitToolTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gsa.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    public void ValidateArtKitCliAcceptsRepositoryPilotKitAndRejectsMissingFolder()
    {
        var kitDir = Path.Combine(FindRepoRoot(), "game", "world", "pilot-kit");
        using var okOut = new StringWriter();
        var exitOk = ToolApplication.Run(["validate-art-kit", kitDir], okOut);
        Assert.Equal(0, exitOk);
        using var okDoc = JsonDocument.Parse(okOut.ToString());
        Assert.True(okDoc.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal(9, okDoc.RootElement.GetProperty("modules").GetArrayLength());
        Assert.Equal(256, okDoc.RootElement.GetProperty("atlas_width").GetInt32());

        var missingDir = Path.Combine(Path.GetTempPath(), $"gsa-missing-kit-{Guid.NewGuid():N}");
        using var errOut = new StringWriter();
        var exitFail = ToolApplication.Run(["validate-art-kit", missingDir], errOut);
        Assert.Equal(2, exitFail);
        using var errDoc = JsonDocument.Parse(errOut.ToString());
        Assert.False(errDoc.RootElement.GetProperty("valid").GetBoolean());
        Assert.Contains(errDoc.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("path").GetString() == Path.GetFullPath(missingDir));
    }
}

