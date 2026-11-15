using System.Text.Json;
using System.Text.Json.Nodes;
using Gsa.Core.World;
using Gsa.Tools.Sky;
using Xunit;

namespace Gsa.Tools.Tests;

public sealed class SkyToolTests
{
    [Fact]
    public void DeclaredSceneProducesAValidatedCacheAndSuccessJson()
    {
        using var fixture = new Fixture();
        using var writer = new StringWriter();
        Assert.Equal(0, SkyTool.Run(["precompute-sky", fixture.Input, fixture.Output], writer));
        using var result = JsonDocument.Parse(writer.ToString());
        Assert.True(result.RootElement.GetProperty("valid").GetBoolean());
        Assert.Equal(0, result.RootElement.GetProperty("diagnostics").GetArrayLength());
        var manifest = WorldSceneManifest.Parse(File.ReadAllText(fixture.Input));
        var cache = SkyVisibilityCache.Parse(File.ReadAllText(fixture.Output));
        cache.ValidateFor(manifest);
        Assert.NotEmpty(cache.Cells);
        Assert.Empty(Directory.GetFiles(fixture.Directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void InvalidSceneReturnsTwoAndPreservesPreviousOutput()
    {
        using var fixture = new Fixture();
        var invalid = JsonNode.Parse(File.ReadAllText(fixture.Input))!;
        invalid["objects"]![0]!["size_enu"]![0] = -1;
        File.WriteAllText(fixture.Input, invalid.ToJsonString());
        File.WriteAllText(fixture.Output, "previous cache");
        using var writer = new StringWriter();
        Assert.Equal(2, SkyTool.Run(["precompute-sky", fixture.Input, fixture.Output], writer));
        Assert.Equal("previous cache", File.ReadAllText(fixture.Output));
        using var result = JsonDocument.Parse(writer.ToString());
        Assert.False(result.RootElement.GetProperty("valid").GetBoolean());
        var diagnostic = Assert.Single(result.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("$.objects[0].size_enu", diagnostic.GetProperty("field").GetString());
        Assert.Equal(Path.GetFullPath(fixture.Input), diagnostic.GetProperty("path").GetString());
    }

    [Fact]
    public void InputCannotBeOverwrittenAsOutput()
    {
        using var fixture = new Fixture();
        var input = File.ReadAllText(fixture.Input);
        using var writer = new StringWriter();
        Assert.Equal(2, SkyTool.Run(["precompute-sky", fixture.Input, fixture.Input], writer));
        Assert.Equal(input, File.ReadAllText(fixture.Input));
    }

    [Fact]
    public void MissingInputDoesNotCreateOutput()
    {
        using var fixture = new Fixture();
        File.Delete(fixture.Input);
        using var writer = new StringWriter();
        Assert.Equal(2, SkyTool.Run(["precompute-sky", fixture.Input, fixture.Output], writer));
        Assert.False(File.Exists(fixture.Output));
    }

    [Fact]
    public void InvalidOutputPathIdentifiesOutputRatherThanInput()
    {
        using var fixture = new Fixture();
        using var writer = new StringWriter();
        Assert.Equal(2, SkyTool.Run(["precompute-sky", fixture.Input, "\0"], writer));
        using var result = JsonDocument.Parse(writer.ToString());
        var diagnostic = Assert.Single(result.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("\0", diagnostic.GetProperty("path").GetString());
        Assert.Equal("$output", diagnostic.GetProperty("field").GetString());
    }

    [Fact]
    public void WrongArgumentsAreUsageErrors()
    {
        using var writer = new StringWriter();
        Assert.Equal(1, SkyTool.Run(["precompute-sky", "only-input"], writer));
        using var result = JsonDocument.Parse(writer.ToString());
        Assert.Equal("$command", result.RootElement.GetProperty("diagnostics")[0].GetProperty("field").GetString());
    }

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; }
        public string Input => Path.Combine(Directory, "scene.json");
        public string Output => Path.Combine(Directory, "sky.json");

        public Fixture()
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "game/world/tutorial-block.json")))
                repository = repository.Parent;
            if (repository is null) throw new InvalidOperationException("Cannot find neutral scene fixture.");
            Directory = Path.Combine(repository.FullName, "work/sky-tool-tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            File.Copy(Path.Combine(repository.FullName, "game/world/tutorial-block.json"), Input);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
