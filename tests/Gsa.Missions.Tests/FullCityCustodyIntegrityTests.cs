using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.World;
using Gsa.Missions.Persistence;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class FullCityCustodyIntegrityTests
{
    private static WorldSceneManifest LoadWorld()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "game", "world", "tutorial-block.json"))) dir = dir.Parent;
        return WorldSceneManifest.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "game", "world", "tutorial-block.json")));
    }

    [Fact]
    public void BenchmarkDisplacementAndStreamingWindowChangesPreserveActualBufferedAndProtectedCapturePayloads()
    {
        var tutorial = LoadWorld();
        var (world, network, _) = FullCityGeodeticNetwork.AssembleFullCity(tutorial);
        var pose = new EstimatedBodyPose(0, new Vector3d(500020, 7400001, 101),
            Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2));
        var transform = CameraGeometry.CameraToMap(pose, CameraMountEstimate.ForwardFacing);
        var scheduler = new CameraCaptureScheduler("city_integrity_camera");
        Assert.True(scheduler.TryEnqueueSimulatedCapture(new CameraCaptureRequest(0, 0,
            transform, transform, transform, pose, CameraMountEstimate.ForwardFacing, CameraEnvironmentSnapshot.DryDay,
            tutorial.Objects.Select(CameraVisibilityCandidate.FromWorldObject).ToArray(), (_, _, _) => false),
            null, out var sample, out _));
        Assert.NotEmpty(sample!.ObservedFeatures);
        var store = new BatchCustodyStore();
        foreach (var id in new[] { "field_batch", "protected_batch" })
        {
            var artifact = SurveyBatchArtifact.FromObservationBatch("city_integrity_contract",
                new ObservationBatch(id, [sample.SensorId], [sample], 1, 0, CaptureStopReason.None));
            Assert.True(store.RegisterBufferedBatch(artifact, 0).Succeeded);
        }
        Assert.True(store.OffloadAtomic("protected_batch", 1).Succeeded);
        var before = store.ExportSnapshot();
        var cadastralBefore = network.Benchmarks.ToDictionary(p => p.Key, p => p.Value.PublishedCadastralPositionEnu);
        var streamer = new OldCenterSectorStreamer(world, activeRadiusMetres: 600, safetyBandRadiusMetres: 600);
        foreach (var id in FullCityGeodeticNetwork.PrimaryBenchmarkIds)
        {
            var reference = network.Benchmarks[id].PublishedCadastralPositionEnu;
            var displacement = network.ApplyClandestineDisplacement(id, new Vector3d(1.6, -1.2, 0));
            Assert.Equal(2, displacement.HorizontalDisplacementMetres);
            Assert.NotEqual(reference, network.Benchmarks[id].PhysicalPositionEnu);
            Assert.Equal(reference, network.Benchmarks[id].PublishedCadastralPositionEnu);
            streamer.UpdateStreamingWindow(network.Benchmarks[id].PhysicalPositionEnu);
            // Inspect real payloads at each boundary, not a count supplied to the traversal helper.
            Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(store.ExportSnapshot()));
            foreach (var entry in store.Entries)
            {
                var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.PayloadJson))).ToLowerInvariant();
                Assert.Equal(entry.PayloadSha256, sha);
                var artifact = JsonSerializer.Deserialize<SurveyBatchArtifact>(entry.PayloadJson)!;
                Assert.Equal(sample.Thumbnail.Sha256, artifact.Samples.Single().ToDomain().Thumbnail.Sha256);
            }
        }
        Assert.Equal(BatchCustodyStage.Buffered, store.Entries.Single(e => e.BatchId == "field_batch").Stage);
        Assert.Equal(BatchCustodyStage.Protected, store.Entries.Single(e => e.BatchId == "protected_batch").Stage);
        Assert.All(cadastralBefore, p => Assert.Equal(p.Value, network.Benchmarks[p.Key].PublishedCadastralPositionEnu));
        var restored = BatchCustodyStore.RecoverAfterCrash(store.ExportSnapshot());
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(restored.ExportSnapshot()));
    }
}
