using System.Diagnostics;
using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.World;

namespace Gsa.Core.Sensors.Lidar;

public sealed record DynamicObjectTemporalSnapshot(
    string ObjectId,
    double TimestampSeconds,
    LidarAnalyticBox Box);

public sealed record ScheduledScanScheduleReport(
    int RenderFps,
    int RenderTickCount,
    int PhysicalScanCount,
    int PrimaryRaysPerSecond,
    int TotalPrimaryRaysCast,
    int TotalSamplesGeoreferenced,
    int DroppedByExpiredTemporalSnapshotCount,
    IReadOnlyList<double> PhysicalScanTimestamps);

public sealed record LidarOptimizationBenchmarkReport(
    int RayCount,
    int WorldBoxCount,
    double BaselineElapsedMs,
    double OptimizedElapsedMs,
    int BaselineHitCount,
    int OptimizedHitCount,
    double MaxPointDriftMetres,
    bool ScientificallyIdentical);

public sealed record OctreeLodInvarianceReport(
    string ContractId,
    int TotalScientificSamples,
    long RamBudgetBytes,
    int LruEvictionCount,
    int BackingReloadCount,
    int Lod250kCount,
    int Lod50kCount,
    int Lod10kCount,
    string BaselineScientificSha256,
    string PostLodAndEvictionScientificSha256,
    bool MetricsInvariantUnderLodAndEviction);

/// <summary>Delivers Level-6 engineering LiDAR acquisition (2x16 @ 5 Hz = 16,000 primary rays/s), FPS-independent scheduling, temporal snapshot safety, and spatially culled optimization (T-F15-05).</summary>
public static class EngineeringVanLidarAcquisitionOptimizer
{
    public const int ExpectedPrimaryRaysPerSecond = 16000;
    public const double DefaultMaxSnapshotSkewSeconds = 0.05;

    public static Func<Vector3d, Vector3d, double, LidarRayHit?> CreateSpatiallyCulledRayCaster(
        IEnumerable<WorldBoxObject> staticBoxes,
        double maxRayRangeMetres = 120.0,
        double gridCellSizeMetres = 40.0)
    {
        ArgumentNullException.ThrowIfNull(staticBoxes);
        var analytic = staticBoxes
            .Select(o => new LidarAnalyticBox(o.Id, o.Material, o.PositionEnu, o.SizeEnu))
            .ToArray();

        return CreateSpatiallyCulledRayCaster(analytic, maxRayRangeMetres, gridCellSizeMetres);
    }

    public static Func<Vector3d, Vector3d, double, LidarRayHit?> CreateSpatiallyCulledRayCaster(
        IReadOnlyList<LidarAnalyticBox> boxes,
        double maxRayRangeMetres = 120.0,
        double gridCellSizeMetres = 40.0)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        var cellMap = new Dictionary<(int Cx, int Cy), List<int>>();

        for (var i = 0; i < boxes.Count; i++)
        {
            var box = boxes[i];
            var hx = box.SizeEnu.X * 0.5;
            var hy = box.SizeEnu.Y * 0.5;
            var minCx = (int)Math.Floor((box.CenterEnu.X - hx) / gridCellSizeMetres);
            var maxCx = (int)Math.Floor((box.CenterEnu.X + hx) / gridCellSizeMetres);
            var minCy = (int)Math.Floor((box.CenterEnu.Y - hy) / gridCellSizeMetres);
            var maxCy = (int)Math.Floor((box.CenterEnu.Y + hy) / gridCellSizeMetres);

            for (var cy = minCy; cy <= maxCy; cy++)
            {
                for (var cx = minCx; cx <= maxCx; cx++)
                {
                    var key = (cx, cy);
                    if (!cellMap.TryGetValue(key, out var list))
                    {
                        list = [];
                        cellMap[key] = list;
                    }
                    list.Add(i);
                }
            }
        }

        return (origin, dir, maxDist) =>
        {
            var range = Math.Min(maxDist, maxRayRangeMetres);
            var endPoint = origin + dir * range;
            var minX = Math.Min(origin.X, endPoint.X);
            var maxX = Math.Max(origin.X, endPoint.X);
            var minY = Math.Min(origin.Y, endPoint.Y);
            var maxY = Math.Max(origin.Y, endPoint.Y);

            var minCx = (int)Math.Floor(minX / gridCellSizeMetres);
            var maxCx = (int)Math.Floor(maxX / gridCellSizeMetres);
            var minCy = (int)Math.Floor(minY / gridCellSizeMetres);
            var maxCy = (int)Math.Floor(maxY / gridCellSizeMetres);

            var tested = new HashSet<int>();
            LidarRayHit? best = null;
            var bestDist = range;

            for (var cy = minCy; cy <= maxCy; cy++)
            {
                for (var cx = minCx; cx <= maxCx; cx++)
                {
                    if (!cellMap.TryGetValue((cx, cy), out var indices))
                        continue;

                    foreach (var idx in indices)
                    {
                        if (!tested.Add(idx))
                            continue;

                        var hit = IntersectSingleAnalyticBox(boxes[idx], origin, dir, bestDist);
                        if (hit is not null && hit.DistanceMetres < bestDist)
                        {
                            bestDist = hit.DistanceMetres;
                            best = hit;
                        }
                    }
                }
            }

            return best;
        };
    }

    public static (bool Resolved, LidarAnalyticBox? SnapshotBox, int DroppedExpiredCount) ResolveDynamicSnapshots(
        double rayTimestampSeconds,
        IEnumerable<DynamicObjectTemporalSnapshot> snapshots,
        double maxSkewSeconds = DefaultMaxSnapshotSkewSeconds)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        DynamicObjectTemporalSnapshot? best = null;
        var bestSkew = double.PositiveInfinity;

        foreach (var snap in snapshots)
        {
            var skew = Math.Abs(rayTimestampSeconds - snap.TimestampSeconds);
            if (skew <= maxSkewSeconds + 1e-9 && skew < bestSkew)
            {
                best = snap;
                bestSkew = skew;
            }
        }

        return best is not null
            ? (true, best.Box, 0)
            : (false, null, 1);
    }

    public static ScheduledScanScheduleReport SimulateAcquisitionAtRenderFps(
        int renderFps,
        double durationSeconds,
        EstimatedBodyTrajectory trajectory,
        Func<Vector3d, Vector3d, double, LidarRayHit?> staticRayCaster,
        IReadOnlyList<DynamicObjectTemporalSnapshot>? dynamicSnapshots = null,
        LidarMountEstimate? mountEstimate = null)
    {
        ArgumentNullException.ThrowIfNull(trajectory);
        ArgumentNullException.ThrowIfNull(staticRayCaster);
        if (renderFps <= 0)
            throw new ArgumentOutOfRangeException(nameof(renderFps));
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));

        var l6 = LidarProfile.L6;
        var mount = mountEstimate ?? LidarMountEstimate.Zero;
        var scanInterval = 1.0 / l6.ScanRateHz;
        var renderDt = 1.0 / renderFps;
        var renderTicks = (int)Math.Round(durationSeconds * renderFps);
        var sim = new LidarBenchSimulator(l6, seed: 20261117UL);

        var scanTimestamps = new List<double>();
        var totalPrimaryRays = 0;
        var totalSamples = 0;
        var droppedDynamic = 0;
        var nextScanTime = 0.0;

        for (var tick = 0; tick < renderTicks; tick++)
        {
            var windowEnd = (tick + 1) * renderDt;
            while (nextScanTime <= windowEnd + 1e-9 && nextScanTime < durationSeconds - 1e-9)
            {
                if (!trajectory.TrySample(nextScanTime, out var pose) || pose is null)
                    break;

                Func<Vector3d, Vector3d, double, LidarRayHit?> activeCaster = staticRayCaster;
                if (dynamicSnapshots is { Count: > 0 })
                {
                    var (resolved, snapBox, dropped) = ResolveDynamicSnapshots(nextScanTime, dynamicSnapshots);
                    if (!resolved)
                        droppedDynamic += dropped;
                    else if (snapBox is not null)
                    {
                        var dynHit = snapBox;
                        activeCaster = (origin, dir, maxDist) =>
                        {
                            var hStatic = staticRayCaster(origin, dir, maxDist);
                            var hDyn = IntersectSingleAnalyticBox(dynHit, origin, dir, maxDist);
                            if (hStatic is null) return hDyn;
                            if (hDyn is null) return hStatic;
                            return hDyn.DistanceMetres < hStatic.DistanceMetres ? hDyn : hStatic;
                        };
                    }
                }

                var trueBodyToMap = new RigidTransform(pose.BodyToMap, pose.PositionEnu);
                var trueLidarToMap = LidarBenchSimulator.ComposeLidarToMap(trueBodyToMap, mount);
                if (sim.TrySimulateScan(nextScanTime, trueLidarToMap, trajectory, mount, isRaining: false, activeCaster, out var scanObs, out _, zeroNoise: true) &&
                    scanObs is not null)
                {
                    scanTimestamps.Add(Math.Round(nextScanTime, 6));
                    totalPrimaryRays += scanObs.PrimaryRaysCast;
                    totalSamples += scanObs.Returns.Count;
                }

                nextScanTime += scanInterval;
            }
        }

        return new ScheduledScanScheduleReport(
            RenderFps: renderFps,
            RenderTickCount: renderTicks,
            PhysicalScanCount: scanTimestamps.Count,
            PrimaryRaysPerSecond: l6.PrimaryRaysPerSecond,
            TotalPrimaryRaysCast: totalPrimaryRays,
            TotalSamplesGeoreferenced: totalSamples,
            DroppedByExpiredTemporalSnapshotCount: droppedDynamic,
            PhysicalScanTimestamps: Array.AsReadOnly(scanTimestamps.ToArray()));
    }

    public static LidarOptimizationBenchmarkReport BenchmarkBaselineVsOptimizedAcquisition(
        IReadOnlyList<LidarAnalyticBox> sceneBoxes,
        Vector3d sensorOriginEnu,
        int scanRevolutions = 1)
    {
        ArgumentNullException.ThrowIfNull(sceneBoxes);
        var l6 = LidarProfile.L6;
        var pattern = l6.BuildScanPattern();
        var rays = new List<Vector3d>(pattern.Count * scanRevolutions);
        for (var rev = 0; rev < scanRevolutions; rev++)
        {
            foreach (var (_, _, dir) in pattern)
                rays.Add(dir);
        }

        var baselineCaster = LidarBenchSimulator.CreateAnalyticBoxRayCaster(sceneBoxes);
        var optimizedCaster = CreateSpatiallyCulledRayCaster(sceneBoxes, l6.MaxRangeMetres);

        var baselineHits = new LidarRayHit?[rays.Count];
        var swBase = Stopwatch.StartNew();
        for (var i = 0; i < rays.Count; i++)
            baselineHits[i] = baselineCaster(sensorOriginEnu, rays[i], l6.MaxRangeMetres);
        swBase.Stop();

        var optimizedHits = new LidarRayHit?[rays.Count];
        var swOpt = Stopwatch.StartNew();
        for (var i = 0; i < rays.Count; i++)
            optimizedHits[i] = optimizedCaster(sensorOriginEnu, rays[i], l6.MaxRangeMetres);
        swOpt.Stop();

        var baseCount = 0;
        var optCount = 0;
        var maxDrift = 0.0;
        var mismatch = false;

        for (var i = 0; i < rays.Count; i++)
        {
            var hb = baselineHits[i];
            var ho = optimizedHits[i];
            if (hb is not null) baseCount++;
            if (ho is not null) optCount++;

            if ((hb is null) != (ho is null))
            {
                mismatch = true;
                continue;
            }

            if (hb is not null && ho is not null)
            {
                var pB = sensorOriginEnu + rays[i] * hb.DistanceMetres;
                var pO = sensorOriginEnu + rays[i] * ho.DistanceMetres;
                maxDrift = Math.Max(maxDrift, (pB - pO).Length);
            }
        }

        return new LidarOptimizationBenchmarkReport(
            RayCount: rays.Count,
            WorldBoxCount: sceneBoxes.Count,
            BaselineElapsedMs: swBase.Elapsed.TotalMilliseconds,
            OptimizedElapsedMs: swOpt.Elapsed.TotalMilliseconds,
            BaselineHitCount: baseCount,
            OptimizedHitCount: optCount,
            MaxPointDriftMetres: maxDrift,
            ScientificallyIdentical: !mismatch && maxDrift < 1e-12);
    }

    public static OctreeLodInvarianceReport VerifyOctreeCqInvarianceAcrossVisualLodAndEviction(
        string contractId,
        IReadOnlyList<GeoreferencedLidarSample> samples,
        long ramBudgetBytes = PointCloudOctreeAndChunkStore.RamCacheBudgetBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        ArgumentNullException.ThrowIfNull(samples);

        var minEnu = new Vector3d(
            samples.Min(s => s.MapPositionEnu.X) - 1.0,
            samples.Min(s => s.MapPositionEnu.Y) - 1.0,
            samples.Min(s => s.MapPositionEnu.Z) - 1.0);
        var maxEnu = new Vector3d(
            samples.Max(s => s.MapPositionEnu.X) + 1.0,
            samples.Max(s => s.MapPositionEnu.Y) + 1.0,
            samples.Max(s => s.MapPositionEnu.Z) + 1.0);

        var store = new PointCloudOctreeAndChunkStore(ramBudgetBytes: ramBudgetBytes);
        store.IngestRevision(samples);

        var baselineScientific = store.QueryScientificSamples(minEnu, maxEnu);
        var baselineHash = ComputeSamplesDigestHex(contractId, baselineScientific);

        var lod250k = store.QueryVisualLod(250_000).Count;
        var lod50k = store.QueryVisualLod(50_000).Count;
        var lod10k = store.QueryVisualLod(10_000).Count;

        // Re-query full scientific precision after visual LOD passes and chunk evictions/reloads.
        var reloadedScientific = store.QueryScientificSamples(minEnu, maxEnu);
        var reloadedHash = ComputeSamplesDigestHex(contractId, reloadedScientific);
        var metrics = store.GetMetrics();

        return new OctreeLodInvarianceReport(
            ContractId: contractId,
            TotalScientificSamples: reloadedScientific.Count,
            RamBudgetBytes: ramBudgetBytes,
            LruEvictionCount: metrics.LruEvictionCount,
            BackingReloadCount: metrics.BackingReloadCount,
            Lod250kCount: lod250k,
            Lod50kCount: lod50k,
            Lod10kCount: lod10k,
            BaselineScientificSha256: baselineHash,
            PostLodAndEvictionScientificSha256: reloadedHash,
            MetricsInvariantUnderLodAndEviction:
                baselineScientific.Count == reloadedScientific.Count &&
                string.Equals(baselineHash, reloadedHash, StringComparison.Ordinal));
    }

    private static LidarRayHit? IntersectSingleAnalyticBox(
        LidarAnalyticBox box,
        Vector3d origin,
        Vector3d dir,
        double maxDist)
    {
        var invX = Math.Abs(dir.X) > 1e-12 ? 1.0 / dir.X : double.PositiveInfinity;
        var invY = Math.Abs(dir.Y) > 1e-12 ? 1.0 / dir.Y : double.PositiveInfinity;
        var invZ = Math.Abs(dir.Z) > 1e-12 ? 1.0 / dir.Z : double.PositiveInfinity;

        var hx = box.SizeEnu.X * 0.5;
        var hy = box.SizeEnu.Y * 0.5;
        var hz = box.SizeEnu.Z * 0.5;
        var min = new Vector3d(box.CenterEnu.X - hx, box.CenterEnu.Y - hy, box.CenterEnu.Z - hz);
        var max = new Vector3d(box.CenterEnu.X + hx, box.CenterEnu.Y + hy, box.CenterEnu.Z + hz);

        if (!Slab(origin.X, invX, min.X, max.X, out var tMinX, out var tMaxX, out var nSignX)) return null;
        if (!Slab(origin.Y, invY, min.Y, max.Y, out var tMinY, out var tMaxY, out var nSignY)) return null;
        if (!Slab(origin.Z, invZ, min.Z, max.Z, out var tMinZ, out var tMaxZ, out var nSignZ)) return null;

        var tEnter = Math.Max(tMinX, Math.Max(tMinY, tMinZ));
        var tExit = Math.Min(tMaxX, Math.Min(tMaxY, tMaxZ));
        if (tEnter > tExit || tExit < 0 || tEnter > maxDist)
            return null;

        var tHit = tEnter >= 0 ? tEnter : tExit;
        if (tHit < 1e-6 || tHit > maxDist)
            return null;

        Vector3d normal;
        if (tMinX >= tMinY && tMinX >= tMinZ)
            normal = new Vector3d(nSignX, 0, 0);
        else if (tMinY >= tMinZ)
            normal = new Vector3d(0, nSignY, 0);
        else
            normal = new Vector3d(0, 0, nSignZ);

        return new LidarRayHit(tHit, normal, box.Material, box.Id);
    }

    private static bool Slab(
        double originCoord, double invDir, double minBound, double maxBound,
        out double tMin, out double tMax, out double enterNormalSign)
    {
        if (double.IsInfinity(invDir))
        {
            tMin = double.NegativeInfinity;
            tMax = double.PositiveInfinity;
            enterNormalSign = 0;
            return originCoord >= minBound && originCoord <= maxBound;
        }

        var t1 = (minBound - originCoord) * invDir;
        var t2 = (maxBound - originCoord) * invDir;
        if (t1 <= t2)
        {
            tMin = t1; tMax = t2; enterNormalSign = -1;
        }
        else
        {
            tMin = t2; tMax = t1; enterNormalSign = 1;
        }
        return true;
    }

    private static string ComputeSamplesDigestHex(string contractId, IReadOnlyList<GeoreferencedLidarSample> samples)
    {
        var ordered = samples.OrderBy(s => s.SampleId).ToArray();
        var sb = new System.Text.StringBuilder(ordered.Length * 48);
        sb.Append(contractId).Append('|').Append(ordered.Length);
        foreach (var s in ordered)
        {
            sb.Append('|')
              .Append(s.SampleId)
              .Append(':')
              .Append(s.MapPositionEnu.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
              .Append(',')
              .Append(s.MapPositionEnu.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
              .Append(',')
              .Append(s.MapPositionEnu.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }
}

