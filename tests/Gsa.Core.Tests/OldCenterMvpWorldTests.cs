using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.World;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class OldCenterMvpWorldTests
{
    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null &&
                   !File.Exists(Path.Combine(directory.FullName, "game", "world", "tutorial-block.json")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    private static WorldSceneManifest LoadTutorialManifest() =>
        WorldSceneManifest.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, "game", "world", "tutorial-block.json")));

    [Fact]
    public void OldCenterMvpMeets500x500Extent25SectorsPreservesTutorialIdsAndTargetInventory()
    {
        var tutorial = LoadTutorialManifest();
        var mvp = OldCenterMvpWorldAssembler.BuildOldCenterMvpManifest(tutorial);

        Assert.Equal("old_center_mvp", mvp.SceneId);
        Assert.Equal(500.0, mvp.Bounds.Max.X - mvp.Bounds.Min.X, 6);
        Assert.Equal(500.0, mvp.Bounds.Max.Y - mvp.Bounds.Min.Y, 6);
        Assert.Equal(25, mvp.Cells.Count);

        foreach (var tutorialObj in tutorial.Objects)
            Assert.Contains(mvp.Objects, o => o.Id == tutorialObj.Id);

        var summary = OldCenterMvpWorldAssembler.SummarizeInventory(mvp);
        Assert.True(summary.MeetsMvpRequirements);
        Assert.True(summary.FacadeCount >= 20);
        Assert.True(summary.ManholeCount >= 12);
        Assert.True(summary.SignCount >= 12);
        Assert.True(summary.SidewalkSegmentCount >= 8);
        Assert.True(summary.OfficeCount >= 1);
        Assert.True(summary.UnloadPointCount >= 2);
        Assert.True(summary.SurveyControlMarkCount >= 2);
        Assert.True(summary.HasTrainingPark);
        Assert.True(summary.HasWifiZone);
        Assert.True(summary.HasCalmArea);

        using var catalog = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryRoot, "content", "regions", "old-center.json")));
        var features = catalog.RootElement.GetProperty("features").EnumerateArray().ToArray();
        Assert.True(features.Count(f => f.GetProperty("class").GetString() == "building_facade") >= 20);
        Assert.True(features.Count(f => f.GetProperty("class").GetString() == "manhole") >= 12);
        Assert.True(features.Count(f => f.GetProperty("class").GetString() == "traffic_sign") >= 12);
        Assert.True(features.Count(f => f.GetProperty("class").GetString() == "sidewalk_segment") >= 8);
    }

    [Fact]
    public void SafetyBandUnloadPausesCaptureAndMissingDistantSectorContinuesSafely()
    {
        var mvp = OldCenterMvpWorldAssembler.BuildOldCenterMvpManifest(LoadTutorialManifest());
        var streamer = new OldCenterSectorStreamer(mvp, activeRadiusMetres: 600.0, safetyBandRadiusMetres: 120.0);

        var playerPos = new Vector3d(500150.0, 7400150.0, 101.0);
        var nominal = streamer.UpdateStreamingWindow(playerPos);
        Assert.True(nominal.CaptureAllowed);
        Assert.False(nominal.SuspendedByUnloadedSafetyBand);
        Assert.Null(nominal.WarningKey);

        var facadeLookup = OldCenterSectorStreamer.TryObserveTarget("facade_01", nominal);
        Assert.True(facadeLookup.Available);
        Assert.NotNull(facadeLookup.TargetObject);

        // Unloading a sector within 120m of player (adjacent sector (2,1)) must pause capture without zero/phantom data
        var forcedNear = new HashSet<WorldSectorCellId> { new(2, 1) };
        var suspended = streamer.UpdateStreamingWindow(playerPos, forcedNear);
        Assert.False(suspended.CaptureAllowed);
        Assert.True(suspended.SuspendedByUnloadedSafetyBand);
        Assert.Equal("world.streaming.safety_band_unloaded", suspended.WarningKey);
        Assert.Contains(new WorldSectorCellId(2, 1), suspended.MissingSafetyBandSectors);

        var pausedLookup = OldCenterSectorStreamer.TryObserveTarget("facade_01", suspended);
        Assert.False(pausedLookup.Available);
        Assert.True(pausedLookup.SuspendedByStreaming);
        Assert.Null(pausedLookup.TargetObject);
        Assert.Equal("world.streaming.safety_band_unloaded", pausedLookup.ReasonKey);

        // Unloading a far corner sector (4,4), >120m from (1,1), must not suspend capture
        var forcedFar = new HashSet<WorldSectorCellId> { new(4, 4) };
        var farUnloaded = streamer.UpdateStreamingWindow(playerPos, forcedFar);
        Assert.True(farUnloaded.CaptureAllowed);
        Assert.False(farUnloaded.SuspendedByUnloadedSafetyBand);
    }

    [Fact]
    public void RegionalPortalsEnforceStoryCampaignPrerequisites()
    {
        var mvp = OldCenterMvpWorldAssembler.BuildOldCenterMvpManifest(LoadTutorialManifest());
        var streamer = new OldCenterSectorStreamer(mvp);

        var completed = new HashSet<string>(StringComparer.Ordinal);
        Assert.False(streamer.EvaluatePortalAccess("portal_porto", completed).Allowed);
        Assert.Equal("world.portal.locked_by_campaign", streamer.EvaluatePortalAccess("portal_porto", completed).ReasonKey);
        Assert.False(streamer.EvaluatePortalAccess("portal_orla", completed).Allowed);
        Assert.False(streamer.EvaluatePortalAccess("portal_serra", completed).Allowed);

        completed.Add("HIST-04");
        Assert.True(streamer.EvaluatePortalAccess("portal_porto", completed).Allowed);
        Assert.False(streamer.EvaluatePortalAccess("portal_orla", completed).Allowed);

        completed.Add("HIST-05");
        Assert.True(streamer.EvaluatePortalAccess("portal_orla", completed).Allowed);
        Assert.False(streamer.EvaluatePortalAccess("portal_serra", completed).Allowed);

        completed.Add("HIST-07");
        Assert.True(streamer.EvaluatePortalAccess("portal_serra", completed).Allowed);
    }
}

