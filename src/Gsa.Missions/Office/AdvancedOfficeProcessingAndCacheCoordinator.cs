using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Sensors.Gnss;
using Gsa.Core.Sensors.Lidar;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Office;

/// <summary>Spatiotemporal &amp; intensity point-cloud outlier filter parameters (strictly non-semantic; never reads truth labels).</summary>
public sealed record SpuriousPointFilterSpec(
    double RadiusMetres = 0.5,
    int MinNeighborsWithinRadius = 3,
    bool RequireMultiPassIfSparse = true,
    float? MinIntensity = null,
    float? MaxIntensity = null);

public sealed record FilteredLidarSampleInput(
    GeoreferencedLidarSample Sample,
    int PassIndex);

public sealed record SpuriousFilterPreviewResult(
    int InputPointCount,
    int RetainedPointCount,
    int RemovedPointCount,
    double ProjectedCoverageFraction,
    double ProjectedMeanDensityPointsPerM2,
    IReadOnlyList<GeoreferencedLidarSample> RetainedSamples);

/// <summary>Identifies a raw Lidar swath batch with its immutable SHA-256 checksum for raw-data preservation guarantees.</summary>
public sealed record RawLidarSwathBatch(
    string BatchId,
    int PassIndex,
    IReadOnlyList<RawLidarRayMeasurement> RawRays)
{
    public string ComputeCanonicalSha256()
    {
        var canonical = JsonSerializer.Serialize(RawRays);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

public sealed record AdvancedProcessingRecipe(
    int SchemaVersion,
    string RecipeId,
    string ContractId,
    ProcessingMode Mode,
    IReadOnlyList<string> RawBatchIds,
    string CalibrationRecipeId,
    int CalibrationRevision,
    SpuriousPointFilterSpec FilterSpec,
    double TotalCalendarSeconds,
    int ServiceFeeCoords)
{
    public string ComputeRecipeHash()
    {
        var canonical = JsonSerializer.Serialize(this);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

public sealed record AdvancedProcessingJobRecord(
    string JobId,
    AdvancedProcessingRecipe Recipe,
    ProcessingJobState State,
    bool FeeDebited,
    int ChargedFeeCoords,
    double StartedAtSeconds,
    double LastClockSeconds,
    double ElapsedCalendarSeconds,
    double TotalCalendarSeconds,
    IReadOnlyDictionary<string, string> RawBatchSha256ById,
    int? ProductRevision,
    string? RecipeHash,
    string? DiagnosticCode);

public sealed record DerivedCloudCacheEntry(
    string CacheKey,
    string JobId,
    int ProductRevision,
    string RecipeHash,
    PointCloudOctreeAndChunkStore OctreeStore,
    PointCloudQualityReport QualityReport,
    double? CheckpointRmseMetres,
    int ValidatedCheckpointCount);

public sealed record AdvancedOfficePipelineOutput(
    bool Succeeded,
    string DiagnosticCode,
    int ProductRevision,
    PpkProcessingResult? PpkResult,
    IReadOnlyList<GeoreferencedLidarSample> UnfilteredSamples,
    IReadOnlyList<GeoreferencedLidarSample> FilteredSamples,
    double? ReservedCheckpointRmseMetres,
    double? MaxCheckpointResidualMetres,
    int ExcludedCqCheckpointCount,
    PointCloudQualityReport? QualityReport);

public sealed record AdvancedOfficeCoordinatorSnapshot(
    int SchemaVersion,
    Dictionary<string, int> RevisionsByContract,
    IReadOnlyList<AdvancedProcessingJobRecord> Jobs,
    string? ActiveJobId);

/// <summary>Non-semantic spatiotemporal/intensity outlier filter with preview and undo (zero access to IsSpuriousRain or truth labels).</summary>
public sealed class PointCloudOutlierFilterSession
{
    private readonly IReadOnlyList<FilteredLidarSampleInput> _initialInputs;
    private readonly Stack<(IReadOnlyList<FilteredLidarSampleInput> Inputs, SpuriousPointFilterSpec? AppliedSpec)> _undoStack = new();

    public IReadOnlyList<FilteredLidarSampleInput> CurrentInputs { get; private set; }
    public SpuriousPointFilterSpec? LastAppliedSpec { get; private set; }
    public bool CanUndo => _undoStack.Count > 0;
    public int UndoDepth => _undoStack.Count;

    public PointCloudOutlierFilterSession(IEnumerable<FilteredLidarSampleInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        _initialInputs = inputs.ToArray();
        CurrentInputs = _initialInputs;
    }

    public SpuriousFilterPreviewResult PreviewFilter(
        SpuriousPointFilterSpec filterSpec,
        PointCloudQualitySpec qualitySpec,
        IReadOnlyList<SurveySurfaceReference> usefulSurfaces)
    {
        ArgumentNullException.ThrowIfNull(filterSpec);
        ArgumentNullException.ThrowIfNull(qualitySpec);
        ArgumentNullException.ThrowIfNull(usefulSurfaces);

        var retained = FilterBySpatiotemporalNeighborhoodAndIntensity(CurrentInputs, filterSpec);
        var retainedSamples = retained.Select(r => r.Sample).ToArray();
        var qc = PointCloudQualityEvaluator.Evaluate(qualitySpec, usefulSurfaces, retainedSamples);

        return new SpuriousFilterPreviewResult(
            InputPointCount: CurrentInputs.Count,
            RetainedPointCount: retained.Count,
            RemovedPointCount: CurrentInputs.Count - retained.Count,
            ProjectedCoverageFraction: qc.CoverageFraction,
            ProjectedMeanDensityPointsPerM2: qc.MeanDensityPointsPerM2,
            RetainedSamples: Array.AsReadOnly(retainedSamples));
    }

    public SpuriousFilterPreviewResult ApplyFilter(
        SpuriousPointFilterSpec filterSpec,
        PointCloudQualitySpec qualitySpec,
        IReadOnlyList<SurveySurfaceReference> usefulSurfaces)
    {
        var preview = PreviewFilter(filterSpec, qualitySpec, usefulSurfaces);
        _undoStack.Push((CurrentInputs, LastAppliedSpec));
        CurrentInputs = FilterBySpatiotemporalNeighborhoodAndIntensity(CurrentInputs, filterSpec);
        LastAppliedSpec = filterSpec;
        return preview;
    }

    public bool TryUndo()
    {
        if (_undoStack.Count == 0)
            return false;

        var prior = _undoStack.Pop();
        CurrentInputs = prior.Inputs;
        LastAppliedSpec = prior.AppliedSpec;
        return true;
    }

    /// <summary>Retains points whose spatial 3D neighborhood, pass count, and intensity satisfy the filter spec. Never inspects truth labels.</summary>
    public static IReadOnlyList<FilteredLidarSampleInput> FilterBySpatiotemporalNeighborhoodAndIntensity(
        IReadOnlyList<FilteredLidarSampleInput> inputs,
        SpuriousPointFilterSpec spec)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(spec);
        if (!double.IsFinite(spec.RadiusMetres) || spec.RadiusMetres <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(spec));
        if (spec.MinNeighborsWithinRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(spec));

        var rSq = spec.RadiusMetres * spec.RadiusMetres;
        var retained = new List<FilteredLidarSampleInput>(inputs.Count);

        for (var i = 0; i < inputs.Count; i++)
        {
            var candidate = inputs[i];
            var intensity = candidate.Sample.Intensity;
            if (spec.MinIntensity.HasValue && intensity < spec.MinIntensity.Value)
                continue;
            if (spec.MaxIntensity.HasValue && intensity > spec.MaxIntensity.Value)
                continue;

            var p = candidate.Sample.MapPositionEnu;
            var neighborCount = 0;
            var distinctPasses = new HashSet<int> { candidate.PassIndex };

            for (var j = 0; j < inputs.Count; j++)
            {
                if (i == j)
                    continue;
                var d = inputs[j].Sample.MapPositionEnu - p;
                var dSq = d.X * d.X + d.Y * d.Y + d.Z * d.Z;
                if (dSq <= rSq + 1e-12)
                {
                    neighborCount++;
                    distinctPasses.Add(inputs[j].PassIndex);
                }
            }

            if (neighborCount < spec.MinNeighborsWithinRadius)
                continue;

            if (spec.RequireMultiPassIfSparse && neighborCount <= spec.MinNeighborsWithinRadius + 1 && distinctPasses.Count < 2)
                continue;

            retained.Add(candidate);
        }

        return Array.AsReadOnly(retained.ToArray());
    }
}

/// <summary>Consolidated mobile-mapping office processing pipeline with persistent recipes, idempotent billing, and derived cache invalidation.</summary>
public sealed class AdvancedOfficeProcessingAndCacheCoordinator
{
    private readonly Dictionary<string, int> _revisionsByContract = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AdvancedProcessingJobRecord> _jobsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DerivedCloudCacheEntry> _derivedCachesByJobId = new(StringComparer.Ordinal);
    private string? _activeJobId;

    public AdvancedProcessingJobRecord? ActiveJob =>
        _activeJobId is not null && _jobsById.TryGetValue(_activeJobId, out var job) ? job : null;

    public IReadOnlyDictionary<string, DerivedCloudCacheEntry> DerivedCaches => _derivedCachesByJobId;

    public AdvancedProcessingJobRecord? GetJob(string jobId) =>
        _jobsById.TryGetValue(jobId, out var job) ? job : null;

    /// <summary>Executes PPK trajectory smoothing, strict CQ checkpoint isolation, LiDAR re-georeferencing, and spatiotemporal outlier filtering.</summary>
    public static AdvancedOfficePipelineOutput ExecutePipeline(
        Vector3d initialPositionEnu,
        Vector3d initialVelocityEnu,
        Quaterniond initialBodyToMapRotation,
        ImuGrade imuGrade,
        IReadOnlyList<ImuObservation> imuObservations,
        IReadOnlyList<CorrectableGnssObservationRecord> roverGnssRecords,
        BaseStationLog? baseStationLog,
        IReadOnlyList<SurveyControlObservation>? surveyControls,
        IReadOnlyList<RawLidarSwathBatch> rawSwathBatches,
        LidarMountEstimate mountEstimate,
        SpuriousPointFilterSpec filterSpec,
        PointCloudQualitySpec qualitySpec,
        IReadOnlyList<SurveySurfaceReference> usefulSurfaces,
        IReadOnlyList<WheelDmiObservation>? dmiObservations = null,
        EvaluationReference? checkpointReference = null,
        IReadOnlyList<SubmittedCheckpointEstimate>? submittedCheckpoints = null,
        int productRevision = 1)
    {
        ArgumentNullException.ThrowIfNull(rawSwathBatches);
        ArgumentNullException.ThrowIfNull(mountEstimate);
        ArgumentNullException.ThrowIfNull(filterSpec);
        ArgumentNullException.ThrowIfNull(qualitySpec);
        ArgumentNullException.ThrowIfNull(usefulSurfaces);

        var reservedIds = new HashSet<string>(StringComparer.Ordinal);
        if (surveyControls is not null)
        {
            foreach (var c in surveyControls)
            {
                if (c.IsCheckpoint)
                    reservedIds.Add(c.PointId);
            }
        }

        var ppk = PpkRtsSmootherAndControlAdjuster.ProcessTrajectory(
            initialPositionEnu,
            initialVelocityEnu,
            initialBodyToMapRotation,
            imuGrade,
            imuObservations,
            roverGnssRecords,
            baseStationLog,
            estimatedRotations: null,
            dmiObservations: dmiObservations,
            surveyControls: surveyControls,
            reservedCqCheckpointIds: reservedIds);

        if (!ppk.Succeeded)
        {
            return new AdvancedOfficePipelineOutput(
                Succeeded: false,
                DiagnosticCode: ppk.DiagnosticCode,
                ProductRevision: productRevision,
                PpkResult: ppk,
                UnfilteredSamples: Array.Empty<GeoreferencedLidarSample>(),
                FilteredSamples: Array.Empty<GeoreferencedLidarSample>(),
                ReservedCheckpointRmseMetres: null,
                MaxCheckpointResidualMetres: null,
                ExcludedCqCheckpointCount: reservedIds.Count,
                QualityReport: null);
        }

        var bodyTrajectory = new EstimatedBodyTrajectory(
            ppk.SmoothedTrajectory.Select(s => s.ToEstimatedBodyPose()));

        // Evaluate reserved checkpoints ONLY AFTER PPK adjustment, verifying strict isolation.
        EvaluateReservedCheckpointsAfterPpk(
            bodyTrajectory,
            surveyControls,
            out var checkpointRmse,
            out var maxCheckpointResidual);

        var unfilteredWithPass = new List<FilteredLidarSampleInput>();
        foreach (var batch in rawSwathBatches)
        {
            var geo = LidarRayGeoreferencer.GeoreferenceRawRays(batch.RawRays, bodyTrajectory, mountEstimate);
            foreach (var sample in geo.Samples)
                unfilteredWithPass.Add(new FilteredLidarSampleInput(sample, batch.PassIndex));
        }

        var filteredWithPass = PointCloudOutlierFilterSession.FilterBySpatiotemporalNeighborhoodAndIntensity(
            unfilteredWithPass,
            filterSpec);
        var filteredSamples = filteredWithPass.Select(f => f.Sample).ToArray();
        var unfilteredSamples = unfilteredWithPass.Select(f => f.Sample).ToArray();

        var qcReport = PointCloudQualityEvaluator.Evaluate(
            qualitySpec,
            usefulSurfaces,
            filteredSamples,
            checkpointReference,
            submittedCheckpoints);

        return new AdvancedOfficePipelineOutput(
            Succeeded: true,
            DiagnosticCode: "office.advanced_pipeline.completed",
            ProductRevision: productRevision,
            PpkResult: ppk,
            UnfilteredSamples: Array.AsReadOnly(unfilteredSamples),
            FilteredSamples: Array.AsReadOnly(filteredSamples),
            ReservedCheckpointRmseMetres: checkpointRmse,
            MaxCheckpointResidualMetres: maxCheckpointResidual,
            ExcludedCqCheckpointCount: reservedIds.Count,
            QualityReport: qcReport);
    }

    public AdvancedProcessingJobRecord EnqueueJob(
        string jobId,
        AdvancedProcessingRecipe recipe,
        IReadOnlyList<RawLidarSwathBatch> rawBatches,
        double nowSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(rawBatches);

        if (_jobsById.TryGetValue(jobId, out var existing))
            return existing;

        var rawHashes = rawBatches.ToDictionary(b => b.BatchId, b => b.ComputeCanonicalSha256(), StringComparer.Ordinal);
        var record = new AdvancedProcessingJobRecord(
            JobId: jobId,
            Recipe: recipe,
            State: ProcessingJobState.Queued,
            FeeDebited: false,
            ChargedFeeCoords: 0,
            StartedAtSeconds: nowSeconds,
            LastClockSeconds: nowSeconds,
            ElapsedCalendarSeconds: 0.0,
            TotalCalendarSeconds: recipe.TotalCalendarSeconds,
            RawBatchSha256ById: rawHashes,
            ProductRevision: null,
            RecipeHash: recipe.ComputeRecipeHash(),
            DiagnosticCode: "office.job.queued");

        _jobsById.Add(jobId, record);
        return record;
    }

    public (bool Succeeded, AdvancedProcessingJobRecord? Job, string ReasonKey) StartOrResumeJob(
        string jobId,
        CareerLedger ledger,
        double nowSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentNullException.ThrowIfNull(ledger);

        if (!_jobsById.TryGetValue(jobId, out var job))
            return (false, null, "office.error.job_not_found");

        if (job.State == ProcessingJobState.Running && string.Equals(_activeJobId, jobId, StringComparison.Ordinal))
            return (true, job, "office.job.already_running_idempotent");

        if (_activeJobId is not null && !string.Equals(_activeJobId, jobId, StringComparison.Ordinal))
            return (false, job, "office.error.queue_busy");

        if (job.State is ProcessingJobState.Completed or ProcessingJobState.Cancelled or ProcessingJobState.FailedExpired)
            return (false, job, "office.error.invalid_terminal_state");

        var debited = job.FeeDebited;
        var charged = job.ChargedFeeCoords;
        if (!debited && job.Recipe.ServiceFeeCoords > 0)
        {
            var txId = $"adv_office_job:{jobId}";
            var debit = ledger.TryDebitPurchaseOrService(
                txId,
                job.Recipe.ContractId,
                nowSeconds,
                job.Recipe.ServiceFeeCoords,
                "office_processing",
                "office.advanced_job.fee");
            if (!debit.Succeeded)
                return (false, job, debit.ReasonKey ?? "ledger.error.insufficient_funds");

            debited = true;
            charged = job.Recipe.ServiceFeeCoords;
        }

        var updated = job with
        {
            State = ProcessingJobState.Running,
            FeeDebited = debited,
            ChargedFeeCoords = charged,
            LastClockSeconds = nowSeconds,
            DiagnosticCode = "office.job.running",
        };
        _jobsById[jobId] = updated;
        _activeJobId = jobId;
        return (true, updated, "office.job.started");
    }

    public AdvancedProcessingJobRecord? CancelJob(string jobId, double nowSeconds, string? reasonKey = null)
    {
        if (!_jobsById.TryGetValue(jobId, out var job))
            return null;
        if (job.State is ProcessingJobState.Completed or ProcessingJobState.Cancelled or ProcessingJobState.FailedExpired)
            return job;

        var cancelled = job with
        {
            State = ProcessingJobState.Cancelled,
            LastClockSeconds = nowSeconds,
            ProductRevision = null,
            DiagnosticCode = reasonKey ?? "office.job.cancelled",
        };
        _jobsById[jobId] = cancelled;
        if (string.Equals(_activeJobId, jobId, StringComparison.Ordinal))
            _activeJobId = null;

        InvalidateDerivedCacheForJob(jobId);
        return cancelled;
    }

    public (AdvancedProcessingJobRecord? Job, DerivedCloudCacheEntry? CacheEntry) CompleteJobWithDerivedCache(
        string jobId,
        double deltaCalendarSeconds,
        IReadOnlyList<GeoreferencedLidarSample> finalScientificCloud,
        PointCloudQualitySpec qualitySpec,
        IReadOnlyList<SurveySurfaceReference> usefulSurfaces,
        double? checkpointRmseMetres = null,
        int validatedCheckpointCount = 0)
    {
        if (!_jobsById.TryGetValue(jobId, out var job) || job.State != ProcessingJobState.Running)
            return (null, null);

        var elapsed = job.ElapsedCalendarSeconds + deltaCalendarSeconds;
        var now = job.LastClockSeconds + deltaCalendarSeconds;
        if (elapsed + 1e-9 < job.TotalCalendarSeconds)
        {
            var running = job with
            {
                LastClockSeconds = now,
                ElapsedCalendarSeconds = elapsed,
            };
            _jobsById[jobId] = running;
            return (running, null);
        }

        var rev = _revisionsByContract.TryGetValue(job.Recipe.ContractId, out var prev) ? prev + 1 : 1;
        _revisionsByContract[job.Recipe.ContractId] = rev;

        var recipeHash = job.Recipe.ComputeRecipeHash();
        var completed = job with
        {
            State = ProcessingJobState.Completed,
            LastClockSeconds = now,
            ElapsedCalendarSeconds = elapsed,
            ProductRevision = rev,
            RecipeHash = recipeHash,
            DiagnosticCode = "office.job.completed",
        };
        _jobsById[jobId] = completed;
        if (string.Equals(_activeJobId, jobId, StringComparison.Ordinal))
            _activeJobId = null;

        // Build fresh derived octree and CQ cache bound to (ProductRevision, RecipeHash)
        var store = new PointCloudOctreeAndChunkStore();
        store.IngestRevision(finalScientificCloud);
        var report = PointCloudQualityEvaluator.EvaluateFromOctreeStore(qualitySpec, usefulSurfaces, store, 100_000);

        var cacheKey = $"{jobId}:r{rev}:{recipeHash[..12]}";
        var cacheEntry = new DerivedCloudCacheEntry(
            cacheKey,
            jobId,
            rev,
            recipeHash,
            store,
            report,
            checkpointRmseMetres,
            validatedCheckpointCount);
        _derivedCachesByJobId[jobId] = cacheEntry;
        return (completed, cacheEntry);
    }

    /// <summary>Checks whether cached derived octree/CQ matches the current recipe hash and calibration revision; invalidates automatically if stale.</summary>
    public bool TryGetValidDerivedCache(
        string jobId,
        AdvancedProcessingRecipe currentRecipe,
        out DerivedCloudCacheEntry? cacheEntry)
    {
        ArgumentNullException.ThrowIfNull(currentRecipe);
        if (!_derivedCachesByJobId.TryGetValue(jobId, out var entry))
        {
            cacheEntry = null;
            return false;
        }

        var currentHash = currentRecipe.ComputeRecipeHash();
        if (!string.Equals(entry.RecipeHash, currentHash, StringComparison.Ordinal) ||
            entry.OctreeStore.RevisionNumber <= 0 ||
            !entry.OctreeStore.DerivedIndicesValid)
        {
            InvalidateDerivedCacheForJob(jobId);
            cacheEntry = null;
            return false;
        }

        cacheEntry = entry;
        return true;
    }

    public void InvalidateDerivedCacheForJob(string jobId)
    {
        if (_derivedCachesByJobId.Remove(jobId, out var removed))
            removed.OctreeStore.InvalidateDerivedIndices();
    }

    public AdvancedOfficeCoordinatorSnapshot ExportSnapshot() =>
        new(
            SchemaVersion: 1,
            RevisionsByContract: new Dictionary<string, int>(_revisionsByContract, StringComparer.Ordinal),
            Jobs: _jobsById.Values.OrderBy(j => j.StartedAtSeconds).ThenBy(j => j.JobId, StringComparer.Ordinal).ToArray(),
            ActiveJobId: _activeJobId);

    public static AdvancedOfficeProcessingAndCacheCoordinator ImportSnapshot(AdvancedOfficeCoordinatorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var coordinator = new AdvancedOfficeProcessingAndCacheCoordinator();
        foreach (var kv in snapshot.RevisionsByContract)
            coordinator._revisionsByContract[kv.Key] = kv.Value;
        foreach (var job in snapshot.Jobs)
            coordinator._jobsById[job.JobId] = job;
        coordinator._activeJobId = snapshot.ActiveJobId;
        return coordinator;
    }

    private static void EvaluateReservedCheckpointsAfterPpk(
        EstimatedBodyTrajectory smoothedTrajectory,
        IReadOnlyList<SurveyControlObservation>? surveyControls,
        out double? rmseMetres,
        out double? maxResidualMetres)
    {
        rmseMetres = null;
        maxResidualMetres = null;
        if (surveyControls is null)
            return;

        var sumSq = 0.0;
        var count = 0;
        var maxErr = 0.0;

        foreach (var ctrl in surveyControls)
        {
            if (!ctrl.IsCheckpoint)
                continue;
            if (!smoothedTrajectory.TrySample(ctrl.TimestampSeconds, out var pose) || pose is null)
                continue;

            var err = (pose.PositionEnu - ctrl.KnownPositionEnu).Length;
            sumSq += err * err;
            if (err > maxErr)
                maxErr = err;
            count++;
        }

        if (count > 0)
        {
            rmseMetres = Math.Sqrt(sumSq / count);
            maxResidualMetres = maxErr;
        }
    }
}
