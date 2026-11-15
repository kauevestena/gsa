using System.Buffers.Binary;
using System.Security.Cryptography;
using Gsa.Core.Geometry;
using Gsa.Core.Randomness;
using Gsa.Core.Sensors.Camera;

namespace Gsa.Core.Sensors.Lidar;

/// <summary>Raw per-ray LiDAR return before trajectory georeferencing (strictly sensor frame, no truth data).</summary>
public sealed record RawLidarRayMeasurement(
    long RaySequence,
    double RawTimestampSeconds,
    double AzimuthRadians,
    double ElevationRadians,
    double MeasuredRangeMetres,
    float Intensity,
    int ReturnNumber,
    int TotalReturnsInPulse,
    bool IsSpuriousRain,
    Vector3d LocalPointLidarMetres);

/// <summary>Double-precision ENU point sample produced by per-ray trajectory + calibration georeferencing.</summary>
public sealed record GeoreferencedLidarSample(
    long SampleId,
    double PhysicalTimestampSeconds,
    Vector3d MapPositionEnu,
    float Intensity,
    int ReturnNumber,
    bool IsSpuriousRain,
    int ClassificationId = 0);

public sealed record LidarGeoreferencingResult(
    bool Succeeded,
    IReadOnlyList<GeoreferencedLidarSample> Samples,
    int RejectedOutOfWindowRays,
    IReadOnlyList<string> DiagnosticCodes,
    double EstimatedLongitudinalSpacingMetres);

/// <summary>Georeferences per-ray LiDAR returns strictly from an estimated trajectory and mount estimate (zero truth access).</summary>
public static class LidarRayGeoreferencer
{
    public static LidarGeoreferencingResult GeoreferenceRawRays(
        IReadOnlyList<RawLidarRayMeasurement> rawRays,
        EstimatedBodyTrajectory estimatedTrajectory,
        LidarMountEstimate mountEstimate,
        double nominalScanLineRateHz = 10.0)
    {
        ArgumentNullException.ThrowIfNull(rawRays);
        ArgumentNullException.ThrowIfNull(estimatedTrajectory);
        ArgumentNullException.ThrowIfNull(mountEstimate);
        if (!double.IsFinite(nominalScanLineRateHz) || nominalScanLineRateHz <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(nominalScanLineRateHz));

        var samples = new List<GeoreferencedLidarSample>(rawRays.Count);
        var diagnostics = new HashSet<string>(StringComparer.Ordinal);
        var rejectedCount = 0;

        EstimatedBodyPose? firstPose = null;
        EstimatedBodyPose? lastPose = null;
        var firstTime = double.PositiveInfinity;
        var lastTime = double.NegativeInfinity;

        foreach (var ray in rawRays)
        {
            var evalTime = ray.RawTimestampSeconds + mountEstimate.TimeOffsetSeconds;
            if (!double.IsFinite(evalTime) ||
                !estimatedTrajectory.TrySample(evalTime, out var estPose) ||
                estPose is null)
            {
                rejectedCount++;
                diagnostics.Add("RAY_TIMESTAMP_OUT_OF_TRAJECTORY_WINDOW");
                continue;
            }

            if (evalTime < firstTime)
            {
                firstTime = evalTime;
                firstPose = estPose;
            }
            if (evalTime > lastTime)
            {
                lastTime = evalTime;
                lastPose = estPose;
            }

            // p_map = r_body(t) + R_body_to_map(t) * (lever_arm_body + R_sensor_to_body * p_sensor)
            var estLidarToMap = LidarBenchSimulator.ComposeLidarToMap(
                new RigidTransform(estPose.BodyToMap, estPose.PositionEnu),
                mountEstimate);
            var mapPoint = estLidarToMap.Transform(ray.LocalPointLidarMetres);

            samples.Add(new GeoreferencedLidarSample(
                ray.RaySequence,
                evalTime,
                mapPoint,
                ray.Intensity,
                ray.ReturnNumber,
                ray.IsSpuriousRain));
        }

        var avgSpacing = 0.0;
        if (firstPose is not null && lastPose is not null && lastTime > firstTime + 1e-9)
        {
            var speedEstimate = (lastPose.PositionEnu - firstPose.PositionEnu).Length / (lastTime - firstTime);
            avgSpacing = speedEstimate / nominalScanLineRateHz;
        }

        return new LidarGeoreferencingResult(
            Succeeded: samples.Count > 0,
            Samples: Array.AsReadOnly(samples.ToArray()),
            RejectedOutOfWindowRays: rejectedCount,
            DiagnosticCodes: Array.AsReadOnly(diagnostics.ToArray()),
            EstimatedLongitudinalSpacingMetres: avgSpacing);
    }
}

/// <summary>Continuous Kombi profiler simulator generating per-ray returns along a moving rig, preserving occlusion and speed spacing.</summary>
public sealed class KombiLidarProfilerAcquisition
{
    private readonly LidarProfile _profile;
    private readonly GaussianRandom _rng;
    private long _nextRaySequence = 1;

    public KombiLidarProfilerAcquisition(LidarProfile? profile = null, ulong seed = 88421UL)
    {
        _profile = profile ?? LidarProfile.L4;
        _rng = new GaussianRandom(seed);
    }

    public IReadOnlyList<RawLidarRayMeasurement> SimulateContinuousSwath(
        double startTimeSeconds,
        int scanLineCount,
        Func<double, RigidTransform> trueLidarToMapAtPhysicalTime,
        Func<Vector3d, Vector3d, double, LidarRayHit?> rayCaster,
        bool isRaining = false,
        bool zeroNoise = false)
    {
        ArgumentNullException.ThrowIfNull(trueLidarToMapAtPhysicalTime);
        ArgumentNullException.ThrowIfNull(rayCaster);
        if (!double.IsFinite(startTimeSeconds) || startTimeSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(startTimeSeconds));
        if (scanLineCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(scanLineCount));

        var pattern = _profile.BuildScanPattern();
        var scanPeriodSeconds = 1.0 / _profile.ScanRateHz;
        var perRayDt = scanPeriodSeconds / Math.Max(1, pattern.Count);
        var effectiveRange = LidarMaterialProperties.GetEffectiveRangeMetres(_profile.MaxRangeMetres, isRaining);

        var rawRays = new List<RawLidarRayMeasurement>(scanLineCount * pattern.Count);

        for (var lineIdx = 0; lineIdx < scanLineCount; lineIdx++)
        {
            var lineStart = startTimeSeconds + lineIdx * scanPeriodSeconds;
            for (var rayIdx = 0; rayIdx < pattern.Count; rayIdx++)
            {
                var rayTime = lineStart + rayIdx * perRayDt;
                var (az, el, dirLidar) = pattern[rayIdx];
                var trueLidarToMap = trueLidarToMapAtPhysicalTime(rayTime);
                var dirMap = trueLidarToMap.Rotation.Rotate(dirLidar).Normalized();

                if (isRaining && !zeroNoise && _rng.NextUniformOpenUnit() < LidarMaterialProperties.RainSpuriousRate)
                {
                    var spuriousRange = Math.Max(0.5, _rng.NextUniformOpenUnit() * effectiveRange * 0.95);
                    rawRays.Add(new RawLidarRayMeasurement(
                        _nextRaySequence++,
                        rayTime,
                        az,
                        el,
                        spuriousRange,
                        0.15f,
                        1,
                        1,
                        true,
                        dirLidar * spuriousRange));
                    continue;
                }

                var hits = TraceSingleOrMultiReturnPulse(trueLidarToMap.Translation, dirMap, effectiveRange, rayCaster, zeroNoise);
                for (var h = 0; h < hits.Count; h++)
                {
                    var (range, intensity) = hits[h];
                    rawRays.Add(new RawLidarRayMeasurement(
                        _nextRaySequence++,
                        rayTime,
                        az,
                        el,
                        range,
                        (float)intensity,
                        h + 1,
                        hits.Count,
                        false,
                        dirLidar * range));
                }
            }
        }

        return Array.AsReadOnly(rawRays.ToArray());
    }

    private List<(double RangeMetres, double Intensity)> TraceSingleOrMultiReturnPulse(
        Vector3d originMap,
        Vector3d dirMap,
        double effectiveMaxRange,
        Func<Vector3d, Vector3d, double, LidarRayHit?> rayCaster,
        bool zeroNoise)
    {
        var accepted = new List<(double, double)>(LidarMaterialProperties.MaxReturnsPerPulse);
        var residualEnergy = 1.0;
        var currentDist = 0.0;
        var seenTargets = new HashSet<string>(StringComparer.Ordinal);

        for (var bounce = 0; bounce < LidarMaterialProperties.MaxReturnsPerPulse; bounce++)
        {
            if (residualEnergy < LidarMaterialProperties.MinResidualEnergy)
                break;

            var remaining = effectiveMaxRange - currentDist;
            if (remaining <= LidarMaterialProperties.VegetationAdvanceMetres)
                break;

            var hit = rayCaster(originMap + dirMap * currentDist, dirMap, remaining);
            if (hit is null || !double.IsFinite(hit.DistanceMetres) || hit.DistanceMetres < 0.0)
                break;

            var totalDist = currentDist + hit.DistanceMetres;
            if (totalDist > effectiveMaxRange)
                break;

            var mat = LidarMaterialProperties.Get(hit.Material);
            if (seenTargets.Add(hit.TargetId))
            {
                var cosIncidence = Math.Abs(Vector3d.Dot(dirMap.Normalized(), hit.NormalMap.Normalized()));
                var prob = LidarMaterialProperties.ComputeReturnProbability(hit.Material, cosIncidence, residualEnergy);
                if (zeroNoise || _rng.NextUniformOpenUnit() <= prob)
                {
                    var rangeNoise = zeroNoise ? 0.0 : _rng.NextGaussian() * _profile.RangeSigmaMetres;
                    var measuredRange = Math.Clamp(totalDist + rangeNoise, 0.05, effectiveMaxRange);
                    accepted.Add((measuredRange, Math.Round(prob, 4)));
                }
            }

            // Opaque materials have TransmissionFactor == 0, preserving occlusion of objects behind them.
            residualEnergy *= mat.TransmissionFactor;
            if (mat.TransmissionFactor <= 0.0 || residualEnergy < LidarMaterialProperties.MinResidualEnergy)
                break;

            currentDist = totalDist + LidarMaterialProperties.VegetationAdvanceMetres;
        }

        return accepted;
    }
}

public sealed record PointChunkMetadata(
    string ChunkId,
    int RevisionNumber,
    int SampleCount,
    Vector3d BoundsMinEnu,
    Vector3d BoundsMaxEnu,
    long ResidentByteSize,
    string ChecksumSha256Hex);

internal sealed record SampleIndexEntry(
    string ChunkId,
    int IndexInChunk,
    int RevisionArrayIndex,
    Vector3d PositionEnu);

internal sealed class OctreeNode
{
    public int Depth { get; }
    public Vector3d Min { get; }
    public Vector3d Max { get; }
    public Vector3d Center { get; }
    public bool IsLeaf => Children is null;
    public List<SampleIndexEntry> SampleRefs { get; } = [];
    public List<List<SampleIndexEntry>> OverflowPages { get; } = [];
    public List<GeoreferencedLidarSample> VisualRepresentatives { get; } = [];
    public OctreeNode[]? Children { get; set; }

    public OctreeNode(int depth, Vector3d min, Vector3d max)
    {
        Depth = depth;
        Min = min;
        Max = max;
        Center = new Vector3d(
            0.5 * (min.X + max.X),
            0.5 * (min.Y + max.Y),
            0.5 * (min.Z + max.Z));
    }

    public int TotalSampleCount =>
        SampleRefs.Count + OverflowPages.Sum(p => p.Count);
}

public sealed record OctreeIndexMetrics(
    int RevisionNumber,
    long TotalSamples,
    int ChunkCount,
    int OctreeNodeCount,
    int LeafNodeCount,
    int MaxObservedDepth,
    int MaxLeafPrimaryBucketCount,
    int OverflowPageCount,
    long CurrentResidentRamBytes,
    long RamBudgetBytes,
    int LruEvictionCount,
    int BackingReloadCount);

/// <summary>Double-precision point cloud store with immutable SHA-256 chunks (&lt;= 65,536), octree (leaf &lt;= 4,096, depth &lt;= 16), &lt;= 256 MiB LRU cache, and &lt;= 250k visual LOD.</summary>
public sealed class PointCloudOctreeAndChunkStore
{
    public const int MaxSamplesPerLeaf = 4096;
    public const int MaxOctreeDepth = 16;
    public const int MaxSamplesPerChunk = 65536;
    public const long RamCacheBudgetBytes = 256L * 1024L * 1024L;
    public const int MaxVisualLodBudgetPoints = 250_000;
    public const int SampleRecordByteSize = 48;

    private readonly long _ramBudgetBytes;
    private readonly long _perSampleCacheWeightBytes;
    private readonly Dictionary<string, PointChunkMetadata> _chunkMeta = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _backingSerializedChunks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GeoreferencedLidarSample[]> _residentChunks = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lruOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _lruNodes = new(StringComparer.Ordinal);

    private OctreeNode? _root;
    private long _currentResidentBytes;
    private int _revisionNumber;
    private long _totalSamples;
    private int _lruEvictionCount;
    private int _backingReloadCount;
    private bool _derivedIndicesValid;

    public int RevisionNumber => _revisionNumber;
    public bool DerivedIndicesValid => _derivedIndicesValid;
    public long CurrentResidentRamBytes => _currentResidentBytes;
    public long ConfiguredRamBudgetBytes => _ramBudgetBytes;
    public int ChunkCount => _chunkMeta.Count;
    public IReadOnlyDictionary<string, PointChunkMetadata> ChunkMetadata => _chunkMeta;

    public PointCloudOctreeAndChunkStore(
        long ramBudgetBytes = RamCacheBudgetBytes,
        long perSampleCacheWeightBytes = SampleRecordByteSize)
    {
        if (ramBudgetBytes <= 0 || ramBudgetBytes > RamCacheBudgetBytes)
            throw new ArgumentOutOfRangeException(nameof(ramBudgetBytes), "RAM cache budget must be in (0, 256 MiB].");
        if (perSampleCacheWeightBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(perSampleCacheWeightBytes));

        _ramBudgetBytes = ramBudgetBytes;
        _perSampleCacheWeightBytes = perSampleCacheWeightBytes;
    }

    public int IngestRevision(IEnumerable<GeoreferencedLidarSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var list = samples.ToArray();

        InvalidateDerivedIndices();
        _chunkMeta.Clear();
        _backingSerializedChunks.Clear();
        _residentChunks.Clear();
        _lruOrder.Clear();
        _lruNodes.Clear();
        _currentResidentBytes = 0;
        _revisionNumber++;
        _totalSamples = list.Length;

        if (list.Length == 0)
        {
            _root = null;
            _derivedIndicesValid = true;
            return _revisionNumber;
        }

        // Compute double-precision spatial bounds with non-degenerate epsilon padding
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var minZ = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        var maxZ = double.NegativeInfinity;

        foreach (var s in list)
        {
            minX = Math.Min(minX, s.MapPositionEnu.X);
            minY = Math.Min(minY, s.MapPositionEnu.Y);
            minZ = Math.Min(minZ, s.MapPositionEnu.Z);
            maxX = Math.Max(maxX, s.MapPositionEnu.X);
            maxY = Math.Max(maxY, s.MapPositionEnu.Y);
            maxZ = Math.Max(maxZ, s.MapPositionEnu.Z);
        }

        var span = Math.Max(0.01, Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ)));
        var rootMin = new Vector3d(minX - 1e-4, minY - 1e-4, minZ - 1e-4);
        var rootMax = new Vector3d(rootMin.X + span + 2e-4, rootMin.Y + span + 2e-4, rootMin.Z + span + 2e-4);
        _root = new OctreeNode(0, rootMin, rootMax);

        // Partition into immutable chunks <= 65,536 samples with SHA-256 checksums
        var chunkIndex = 0;
        for (var offset = 0; offset < list.Length; offset += MaxSamplesPerChunk)
        {
            var count = Math.Min(MaxSamplesPerChunk, list.Length - offset);
            var chunkSamples = new GeoreferencedLidarSample[count];
            Array.Copy(list, offset, chunkSamples, 0, count);

            var cMinX = double.PositiveInfinity;
            var cMinY = double.PositiveInfinity;
            var cMinZ = double.PositiveInfinity;
            var cMaxX = double.NegativeInfinity;
            var cMaxY = double.NegativeInfinity;
            var cMaxZ = double.NegativeInfinity;
            for (var i = 0; i < count; i++)
            {
                var p = chunkSamples[i].MapPositionEnu;
                cMinX = Math.Min(cMinX, p.X);
                cMinY = Math.Min(cMinY, p.Y);
                cMinZ = Math.Min(cMinZ, p.Z);
                cMaxX = Math.Max(cMaxX, p.X);
                cMaxY = Math.Max(cMaxY, p.Y);
                cMaxZ = Math.Max(cMaxZ, p.Z);
            }

            var chunkId = $"rev{_revisionNumber:D3}_chunk_{chunkIndex:D4}";
            var serialized = SerializeChunk(chunkSamples);
            var checksum = ComputeSha256Hex(serialized);
            var residentBytes = count * _perSampleCacheWeightBytes;

            if (residentBytes > _ramBudgetBytes)
                throw new InvalidOperationException("Single chunk size exceeds configured LRU RAM budget.");

            var meta = new PointChunkMetadata(
                chunkId,
                _revisionNumber,
                count,
                new Vector3d(cMinX, cMinY, cMinZ),
                new Vector3d(cMaxX, cMaxY, cMaxZ),
                residentBytes,
                checksum);

            _chunkMeta[chunkId] = meta;
            _backingSerializedChunks[chunkId] = serialized;
            CacheChunkInRam(chunkId, chunkSamples, residentBytes);

            for (var i = 0; i < count; i++)
            {
                var entry = new SampleIndexEntry(chunkId, i, offset + i, chunkSamples[i].MapPositionEnu);
                InsertIntoOctree(_root, entry);
            }

            chunkIndex++;
        }

        BuildVisualRepresentatives(_root, list);
        _derivedIndicesValid = true;
        return _revisionNumber;
    }

    public void InvalidateDerivedIndices()
    {
        _root = null;
        _derivedIndicesValid = false;
    }

    /// <summary>Corrupts the serialized backing payload for integrity testing of SHA-256 verification on LRU reload.</summary>
    public void CorruptBackingChunkBytesForVerificationTest(string chunkId)
    {
        if (!_backingSerializedChunks.TryGetValue(chunkId, out var bytes) || bytes.Length == 0)
            throw new KeyNotFoundException($"Chunk '{chunkId}' not found.");
        bytes[0] ^= 0xFF;
    }

    public bool IsChunkResidentInRam(string chunkId) => _residentChunks.ContainsKey(chunkId);

    /// <summary>Returns visual LOD points capped at &lt;= 250,000 without mutating scientific samples.</summary>
    public IReadOnlyList<GeoreferencedLidarSample> QueryVisualLod(int requestedBudgetPoints = MaxVisualLodBudgetPoints)
    {
        if (requestedBudgetPoints <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedBudgetPoints));
        if (!_derivedIndicesValid || _root is null)
            return Array.Empty<GeoreferencedLidarSample>();

        var effectiveBudget = Math.Min(requestedBudgetPoints, MaxVisualLodBudgetPoints);
        var result = new List<GeoreferencedLidarSample>(Math.Min(effectiveBudget, (int)Math.Min(int.MaxValue, _totalSamples)));
        var queue = new Queue<OctreeNode>();
        queue.Enqueue(_root);

        while (queue.Count > 0 && result.Count < effectiveBudget)
        {
            var node = queue.Dequeue();
            foreach (var rep in node.VisualRepresentatives)
            {
                result.Add(rep);
                if (result.Count >= effectiveBudget)
                    break;
            }

            if (node.Children is not null && result.Count < effectiveBudget)
            {
                foreach (var child in node.Children)
                {
                    if (child.IsLeaf ? child.TotalSampleCount > 0 : HasAnySamples(child))
                        queue.Enqueue(child);
                }
            }
        }

        return Array.AsReadOnly(result.ToArray());
    }

    /// <summary>Queries exact unmodified double-precision scientific samples within the region, paging through LRU cache with SHA-256 verification.</summary>
    public IReadOnlyList<GeoreferencedLidarSample> QueryScientificSamples(Vector3d regionMinEnu, Vector3d regionMaxEnu)
    {
        if (!_derivedIndicesValid || _root is null)
            return Array.Empty<GeoreferencedLidarSample>();

        var matching = new List<GeoreferencedLidarSample>();
        CollectScientificSamplesInBox(_root, regionMinEnu, regionMaxEnu, matching);
        return Array.AsReadOnly(matching.ToArray());
    }

    public OctreeIndexMetrics GetMetrics()
    {
        var nodeCount = 0;
        var leafCount = 0;
        var maxDepth = 0;
        var maxLeafBucket = 0;
        var overflowPages = 0;

        if (_root is not null)
        {
            var stack = new Stack<OctreeNode>();
            stack.Push(_root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                nodeCount++;
                maxDepth = Math.Max(maxDepth, n.Depth);
                if (n.IsLeaf)
                {
                    leafCount++;
                    maxLeafBucket = Math.Max(maxLeafBucket, n.SampleRefs.Count);
                    overflowPages += n.OverflowPages.Count;
                }
                else if (n.Children is not null)
                {
                    foreach (var c in n.Children)
                        stack.Push(c);
                }
            }
        }

        return new OctreeIndexMetrics(
            _revisionNumber,
            _totalSamples,
            _chunkMeta.Count,
            nodeCount,
            leafCount,
            maxDepth,
            maxLeafBucket,
            overflowPages,
            _currentResidentBytes,
            _ramBudgetBytes,
            _lruEvictionCount,
            _backingReloadCount);
    }

    private void InsertIntoOctree(OctreeNode node, SampleIndexEntry entry)
    {
        while (!node.IsLeaf)
        {
            var octant = GetOctant(node.Center, entry.PositionEnu);
            node = node.Children![octant];
        }

        if (node.SampleRefs.Count < MaxSamplesPerLeaf)
        {
            node.SampleRefs.Add(entry);
            return;
        }

        if (node.Depth < MaxOctreeDepth)
        {
            Subdivide(node);
            var octant = GetOctant(node.Center, entry.PositionEnu);
            InsertIntoOctree(node.Children![octant], entry);
            return;
        }

        // At MaxOctreeDepth (16), retain all additional samples in overflow pages instead of dropping or exceeding depth 16.
        if (node.OverflowPages.Count == 0 || node.OverflowPages[^1].Count >= MaxSamplesPerLeaf)
            node.OverflowPages.Add([]);
        node.OverflowPages[^1].Add(entry);
    }

    private static void Subdivide(OctreeNode node)
    {
        var children = new OctreeNode[8];
        var min = node.Min;
        var mid = node.Center;
        var max = node.Max;
        var childDepth = node.Depth + 1;

        for (var i = 0; i < 8; i++)
        {
            var cxMin = (i & 1) == 0 ? min.X : mid.X;
            var cxMax = (i & 1) == 0 ? mid.X : max.X;
            var cyMin = (i & 2) == 0 ? min.Y : mid.Y;
            var cyMax = (i & 2) == 0 ? mid.Y : max.Y;
            var czMin = (i & 4) == 0 ? min.Z : mid.Z;
            var czMax = (i & 4) == 0 ? mid.Z : max.Z;
            children[i] = new OctreeNode(childDepth, new Vector3d(cxMin, cyMin, czMin), new Vector3d(cxMax, cyMax, czMax));
        }

        node.Children = children;
        foreach (var existing in node.SampleRefs)
        {
            var octant = GetOctant(node.Center, existing.PositionEnu);
            children[octant].SampleRefs.Add(existing);
        }
        node.SampleRefs.Clear();
    }

    private static int GetOctant(Vector3d center, Vector3d point)
    {
        var idx = 0;
        if (point.X >= center.X) idx |= 1;
        if (point.Y >= center.Y) idx |= 2;
        if (point.Z >= center.Z) idx |= 4;
        return idx;
    }

    private void BuildVisualRepresentatives(OctreeNode node, GeoreferencedLidarSample[] sourceList)
    {
        const int maxRepsPerNode = 16;
        node.VisualRepresentatives.Clear();

        if (node.IsLeaf)
        {
            var total = node.TotalSampleCount;
            if (total == 0) return;
            var stride = Math.Max(1, node.SampleRefs.Count / maxRepsPerNode);
            for (var i = 0; i < node.SampleRefs.Count && node.VisualRepresentatives.Count < maxRepsPerNode; i += stride)
            {
                var r = node.SampleRefs[i];
                node.VisualRepresentatives.Add(sourceList[r.RevisionArrayIndex]);
            }
            return;
        }

        foreach (var child in node.Children!)
        {
            BuildVisualRepresentatives(child, sourceList);
            if (child.VisualRepresentatives.Count > 0 && node.VisualRepresentatives.Count < maxRepsPerNode)
                node.VisualRepresentatives.Add(child.VisualRepresentatives[0]);
        }
    }

    private void CollectScientificSamplesInBox(
        OctreeNode node,
        Vector3d qMin,
        Vector3d qMax,
        List<GeoreferencedLidarSample> output)
    {
        if (!Intersects(node.Min, node.Max, qMin, qMax))
            return;

        if (node.IsLeaf)
        {
            foreach (var r in node.SampleRefs)
            {
                if (Contains(qMin, qMax, r.PositionEnu))
                    output.Add(LoadChunkSample(r.ChunkId, r.IndexInChunk));
            }
            foreach (var page in node.OverflowPages)
            {
                foreach (var r in page)
                {
                    if (Contains(qMin, qMax, r.PositionEnu))
                        output.Add(LoadChunkSample(r.ChunkId, r.IndexInChunk));
                }
            }
            return;
        }

        foreach (var child in node.Children!)
            CollectScientificSamplesInBox(child, qMin, qMax, output);
    }

    private GeoreferencedLidarSample LoadChunkSample(string chunkId, int indexInChunk)
    {
        if (_residentChunks.TryGetValue(chunkId, out var samples))
        {
            TouchLru(chunkId);
            return samples[indexInChunk];
        }

        var meta = _chunkMeta[chunkId];
        var serialized = _backingSerializedChunks[chunkId];
        var actualHash = ComputeSha256Hex(serialized);
        if (!string.Equals(actualHash, meta.ChecksumSha256Hex, StringComparison.Ordinal))
            throw new InvalidDataException($"Point cloud chunk '{chunkId}' failed SHA-256 integrity check on reload.");

        var deserialized = DeserializeChunk(serialized, meta.SampleCount);
        _backingReloadCount++;
        CacheChunkInRam(chunkId, deserialized, meta.ResidentByteSize);
        return deserialized[indexInChunk];
    }

    private void CacheChunkInRam(string chunkId, GeoreferencedLidarSample[] samples, long byteSize)
    {
        while (_currentResidentBytes + byteSize > _ramBudgetBytes && _lruOrder.Count > 0)
        {
            var oldestKey = _lruOrder.Last!.Value;
            _lruOrder.RemoveLast();
            _lruNodes.Remove(oldestKey);
            if (_residentChunks.Remove(oldestKey))
            {
                _currentResidentBytes -= _chunkMeta[oldestKey].ResidentByteSize;
                _lruEvictionCount++;
            }
        }

        _residentChunks[chunkId] = samples;
        var lruNode = _lruOrder.AddFirst(chunkId);
        _lruNodes[chunkId] = lruNode;
        _currentResidentBytes += byteSize;
    }

    private void TouchLru(string chunkId)
    {
        if (_lruNodes.TryGetValue(chunkId, out var node) && node != _lruOrder.First)
        {
            _lruOrder.Remove(node);
            _lruOrder.AddFirst(node);
        }
    }

    private static bool Intersects(Vector3d aMin, Vector3d aMax, Vector3d bMin, Vector3d bMax) =>
        aMax.X >= bMin.X && aMin.X <= bMax.X &&
        aMax.Y >= bMin.Y && aMin.Y <= bMax.Y &&
        aMax.Z >= bMin.Z && aMin.Z <= bMax.Z;

    private static bool Contains(Vector3d bMin, Vector3d bMax, Vector3d p) =>
        p.X >= bMin.X && p.X <= bMax.X &&
        p.Y >= bMin.Y && p.Y <= bMax.Y &&
        p.Z >= bMin.Z && p.Z <= bMax.Z;

    private static bool HasAnySamples(OctreeNode node)
    {
        if (node.IsLeaf) return node.TotalSampleCount > 0;
        return node.Children!.Any(HasAnySamples);
    }

    private static byte[] SerializeChunk(GeoreferencedLidarSample[] samples)
    {
        var buffer = new byte[samples.Length * SampleRecordByteSize];
        var span = buffer.AsSpan();
        for (var i = 0; i < samples.Length; i++)
        {
            var s = samples[i];
            var off = i * SampleRecordByteSize;
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(off, 8), s.SampleId);
            BinaryPrimitives.WriteDoubleLittleEndian(span.Slice(off + 8, 8), s.PhysicalTimestampSeconds);
            BinaryPrimitives.WriteDoubleLittleEndian(span.Slice(off + 16, 8), s.MapPositionEnu.X);
            BinaryPrimitives.WriteDoubleLittleEndian(span.Slice(off + 24, 8), s.MapPositionEnu.Y);
            BinaryPrimitives.WriteDoubleLittleEndian(span.Slice(off + 32, 8), s.MapPositionEnu.Z);
            BinaryPrimitives.WriteSingleLittleEndian(span.Slice(off + 40, 4), s.Intensity);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(off + 44, 4),
                (s.ClassificationId & 0xFFFF) | ((s.ReturnNumber & 0xFF) << 16) | (s.IsSpuriousRain ? 1 << 24 : 0));
        }
        return buffer;
    }

    private static GeoreferencedLidarSample[] DeserializeChunk(byte[] buffer, int count)
    {
        var result = new GeoreferencedLidarSample[count];
        var span = buffer.AsSpan();
        for (var i = 0; i < count; i++)
        {
            var off = i * SampleRecordByteSize;
            var id = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(off, 8));
            var t = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(off + 8, 8));
            var x = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(off + 16, 8));
            var y = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(off + 24, 8));
            var z = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(off + 32, 8));
            var intensity = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(off + 40, 4));
            var packed = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(off + 44, 4));
            var classId = packed & 0xFFFF;
            var returnNum = (packed >> 16) & 0xFF;
            var isRain = (packed & (1 << 24)) != 0;
            result[i] = new GeoreferencedLidarSample(id, t, new Vector3d(x, y, z), intensity, returnNum, isRain, classId);
        }
        return result;
    }

    private static string ComputeSha256Hex(ReadOnlySpan<byte> payload)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(payload, hash);
        return Convert.ToHexString(hash);
    }
}
