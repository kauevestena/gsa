using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Lidar;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class LidarPerformanceRegressionTests
{
    [Fact]
    public void OverlappingChunksLoadAtMostOncePerQueryAndKeepExactSamplesAndTraversalOrder()
    {
        var samples = Enumerable.Range(0, PointCloudOctreeAndChunkStore.MaxSamplesPerChunk * 2)
            .Select(i => new GeoreferencedLidarSample(i + 1, i * 0.0001,
                new(500000 + i % 256, 7400000 + (i % 65536) / 256, 100 + (i % 5) * 0.05),
                0.8f, 1, false)).ToArray();
        var resident = new PointCloudOctreeAndChunkStore();
        var paged = new PointCloudOctreeAndChunkStore(
            ramBudgetBytes: PointCloudOctreeAndChunkStore.MaxSamplesPerChunk * PointCloudOctreeAndChunkStore.SampleRecordByteSize);
        resident.IngestRevision(samples);
        paged.IngestRevision(samples);
        var min = new Vector3d(500000, 7400000, 99);
        var max = new Vector3d(500000, 7400255, 101);
        var expected = resident.QueryScientificSamples(min, max);
        Assert.Equal(512, expected.Count);

        for (var i = 0; i < 2; i++)
        {
            var reloads = paged.GetMetrics().BackingReloadCount;
            var actual = paged.QueryScientificSamples(min, max);
            Assert.Equal(expected, actual);
            Assert.Equal(512, actual.Select(s => s.SampleId).Distinct().Count());
            Assert.All(actual, s => Assert.Equal(samples[(int)s.SampleId - 1], s));
            // The old leaf-by-leaf fetch reloaded these same two chunks 16 times.
            Assert.InRange(paged.GetMetrics().BackingReloadCount - reloads, 0, paged.ChunkCount);
            Assert.True(paged.CurrentResidentRamBytes <= paged.ConfiguredRamBudgetBytes);
        }

        var beforeEmpty = paged.GetMetrics().BackingReloadCount;
        Assert.Empty(paged.QueryScientificSamples(new(500000, 7400000, 500), new(500255, 7400255, 501)));
        Assert.Equal(beforeEmpty, paged.GetMetrics().BackingReloadCount);

        // Grouping must still verify the backing payload on every actual reload.
        Assert.False(paged.IsChunkResidentInRam("rev001_chunk_0000"));
        paged.CorruptBackingChunkBytesForVerificationTest("rev001_chunk_0000");
        Assert.Throws<InvalidDataException>(() => paged.QueryScientificSamples(min, max));
    }

    [Fact]
    public void ReusingPulseScratchKeepsSeededRainMultiReturnAndSuccessiveSwathsIdentical()
    {
        var caster = LidarBenchSimulator.CreateAnalyticBoxRayCaster(new LidarAnalyticBox[]
        {
            new("hall", "concrete", new(500000, 7400000, 102), new(30, 30, 10)),
            new("glass", "glass", new(500006, 7400000, 102), new(0.1, 24, 8)),
        });
        static RigidTransform Pose(double t) => new(Quaterniond.Identity, new(500000 + t, 7400000, 101));
        var whole = new KombiLidarProfilerAcquisition(LidarProfile.L6, seed: 88421UL)
            .SimulateContinuousSwath(0, 2, Pose, caster, isRaining: true);
        var split = new KombiLidarProfilerAcquisition(LidarProfile.L6, seed: 88421UL);
        var first = split.SimulateContinuousSwath(0, 1, Pose, caster, isRaining: true);
        var snapshot = first.ToArray();
        var second = split.SimulateContinuousSwath(1.0 / LidarProfile.L6.ScanRateHz, 1, Pose, caster, isRaining: true);

        Assert.Equal(whole, first.Concat(second));
        Assert.Equal(snapshot, first);
        Assert.Contains(whole, ray => ray.IsSpuriousRain);
        Assert.Contains(whole, ray => ray.TotalReturnsInPulse > 1);
        Assert.All(whole, ray => Assert.InRange(ray.ReturnNumber, 1, ray.TotalReturnsInPulse));
    }
}
