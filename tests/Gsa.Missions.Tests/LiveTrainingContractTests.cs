using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.World;
using Gsa.Missions.Contracts;
using Gsa.Missions.Gameplay;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class LiveTrainingContractTests
{
    private static WorldSceneManifest World()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "game", "world", "tutorial-block.json"))) dir = dir.Parent;
        return WorldSceneManifest.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "game", "world", "tutorial-block.json")));
    }

    private static CameraObservationSample Capture(WorldSceneManifest world, double east, double heading, string id, bool occluded = false)
    {
        var pose = new EstimatedBodyPose(0, new Vector3d(east, 7400000.7, 101), Quaterniond.FromAxisAngle(Vector3d.UnitZ, heading));
        var transform = CameraGeometry.CameraToMap(pose, CameraMountEstimate.ForwardFacing);
        var scheduler = new CameraCaptureScheduler(id);
        Assert.True(scheduler.TryEnqueueSimulatedCapture(new CameraCaptureRequest(0, 0,
            transform, transform, transform, pose, CameraMountEstimate.ForwardFacing, CameraEnvironmentSnapshot.DryDay,
            world.Objects.Select(CameraVisibilityCandidate.FromWorldObject).ToArray(), (_, _, _) => occluded), null, out var sample, out _));
        return sample!;
    }

    [Fact]
    public void LiveObservationsRequireParallaxOffloadAndProcessingBeforeIdempotentPaymentSurvivesReload()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var world = World();
            var session = new TrainingContractSession(world, dir);
            var office = TrainingContractSession.SpawnPosition;
            var field = new Vector3d(500190, 7400000.7, 100);
            Assert.False(session.Accept(field));
            Assert.True(session.Accept(office));
            Assert.False(session.Process(office));
            Assert.False(session.Deliver(office));
            var first = Capture(world, 500188, Math.PI / 2, "first");
            Assert.True(session.RecordObservation(first));
            Assert.False(session.RecordObservation(first));
            Assert.Empty(session.ResolvedTargetIds); // A single image must not acquire world depth.
            session.Save();
            session = new TrainingContractSession(world, dir);
            Assert.True(session.Load().Succeeded);
            Assert.Equal(1, session.PendingCount);
            Assert.Equal(first.Thumbnail.Sha256, session.Samples[0].Thumbnail.Sha256);
            Assert.False(session.Offload(field));
            Assert.False(session.Process(office));
            Assert.True(session.Offload(office));
            Assert.False(session.Process(field));
            Assert.True(session.Process(office));
            Assert.Equal(ContractState.Retrabalho, session.Contract.State);
            Assert.False(session.Deliver(office));
            foreach (var (east, index) in new[] { 500191.0, 500198.0, 500201.0, 500208.0, 500211.0 }.Select((x, i) => (x, i)))
                Assert.True(session.RecordObservation(Capture(world, east, Math.PI / 2, $"next{index}")));
            Assert.Equal(3, session.ResolvedTargetIds.Count);
            Assert.True(session.Offload(office));
            var hashes = session.Custody.Entries.Select(e => e.PayloadSha256).ToArray();
            Assert.True(session.Process(office));
            Assert.True(session.Report!.Passed);
            Assert.Equal(3, session.Report.MatchedFeatureCount);
            Assert.Equal(hashes, session.Custody.Entries.Select(e => e.PayloadSha256).ToArray());
            Assert.True(session.Load().Succeeded);
            Assert.True(session.Report!.Passed);
            Assert.True(session.Deliver(office));
            var balance = session.Ledger.BalanceCoords;
            var xp = session.Progression.TotalXp;
            session = new TrainingContractSession(world, dir);
            Assert.True(session.Load().Succeeded);
            Assert.True(session.Deliver(office));
            Assert.Equal(balance, session.Ledger.BalanceCoords);
            Assert.Equal(xp, session.Progression.TotalXp);
            Assert.Single(session.Ledger.Entries, e => e.TransactionId.StartsWith("settlement:", StringComparison.Ordinal));
            Assert.False(session.RecordObservation(first));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void OccludedAndRepeatedStationaryViewsCannotCompleteTrainingAndCorruptLoadPreservesSession()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var world = World();
            var session = new TrainingContractSession(world, dir);
            Assert.True(session.Accept(TrainingContractSession.SpawnPosition));
            Assert.True(session.RecordObservation(Capture(world, 500188, Math.PI / 2, "blocked", occluded: true)));
            Assert.True(session.RecordObservation(Capture(world, 500188, Math.PI / 2, "same1")));
            Assert.True(session.RecordObservation(Capture(world, 500188, Math.PI / 2, "same2")));
            Assert.Empty(session.ResolvedTargetIds);
            session.Save();
            var path = Path.Combine(dir, "current.save.json");
            File.WriteAllText(path, "broken-save");
            Assert.False(session.Load().Succeeded);
            Assert.Equal(3, session.SampleCount);
            Assert.True(session.SaveBlocked);
            Assert.Throws<IOException>(() => session.Save());
            Assert.Equal("broken-save", File.ReadAllText(path));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
