using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class GimbalinaPersistenceTests
{
    [Fact]
    public void ReloadPreservesOscillationAndFutureMotionExactly()
    {
        var first = new GimbalinaStabilizer(85); first.Step(.123, false, .2, -.1);
        var restored = GimbalinaStabilizer.FromSaveSnapshot(JsonSerializer.Deserialize<GimbalinaSaveSnapshot>(JsonSerializer.Serialize(first.ExportSaveSnapshot()))!);
        Assert.Equal(first.ExportSaveSnapshot(), restored.ExportSaveSnapshot());
        Assert.Equal(first.ComputeEffectiveBodyToMap(Quaterniond.Identity, .01), restored.ComputeEffectiveBodyToMap(Quaterniond.Identity, .01));
        Assert.Equal(first.Step(.2), restored.Step(.2));
    }
    [Fact]
    public void ReloadDoesNotBypassCornCooldownOrResurrectFledChicken()
    {
        var s = new GimbalinaStabilizer(85); Assert.True(s.TryFeedCorn()); s.Step(4.5);
        s = GimbalinaStabilizer.FromSaveSnapshot(s.ExportSaveSnapshot()); Assert.False(s.TryFeedCorn());
        s.Step(5.5); Assert.True(s.TryFeedCorn());
        s = new(100); s.Step(5.1); Assert.True(s.HasFledToSafety);
        s = GimbalinaStabilizer.FromSaveSnapshot(s.ExportSaveSnapshot()); s.Step(20);
        Assert.True(s.HasFledToSafety); Assert.False(s.Injured); Assert.Equal(1, s.SafeEscapeCount);
        s.RecoverAtOffice(); Assert.False(s.HasFledToSafety); Assert.Equal(0, s.Stress);
    }
    [Fact]
    public void FutureAndInvalidMotionStatesAreRejected()
    {
        var s = new GimbalinaStabilizer().ExportSaveSnapshot();
        Assert.Throws<ArgumentException>(() => GimbalinaStabilizer.FromSaveSnapshot(s with { SchemaVersion = 2 }));
        Assert.Throws<ArgumentException>(() => GimbalinaStabilizer.FromSaveSnapshot(s with { OscillationPhaseRadians = double.NaN }));
        Assert.Throws<ArgumentException>(() => GimbalinaStabilizer.FromSaveSnapshot(s with { HasFledToSafety = true }));
    }
}
