using System.Reflection;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Calibration;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Sensors.Gnss;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.Traffic;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class M5CloudCqOfficeLidarWorkshopAndSabotageTests
{
    [Fact]
    public void IndexedOutlierFilterMatchesExhaustiveNeighborsWithoutRainLabels()
    {
        var random = new Random(3923);
        var inputs = Enumerable.Range(1, 300).Select(i => new FilteredLidarSampleInput(
            new GeoreferencedLidarSample(i, i * .01,
                new(501350 + random.NextDouble() * 5, 7400836 + random.NextDouble() * 5, random.NextDouble() - .5),
                i % 7 == 0 ? .15f : .7f, 1, i % 3 == 0), i % 4)).ToArray();

        foreach (var spec in new[] { new SpuriousPointFilterSpec(.45, 2, false, .24f), new(.45, 3, true), new(.9, 0, false) })
        {
            double maxDistanceSquared = spec.RadiusMetres * spec.RadiusMetres + 1e-12;
            var expected = inputs.Where(candidate =>
            {
                if (spec.MinIntensity is { } minimum && candidate.Sample.Intensity < minimum) return false;
                var neighbors = inputs.Where(other => other.Sample.SampleId != candidate.Sample.SampleId)
                    .Where(other =>
                    {
                        var delta = other.Sample.MapPositionEnu - candidate.Sample.MapPositionEnu;
                        return Vector3d.Dot(delta, delta) <= maxDistanceSquared;
                    }).ToArray();
                return neighbors.Length >= spec.MinNeighborsWithinRadius && (!spec.RequireMultiPassIfSparse ||
                    neighbors.Length > spec.MinNeighborsWithinRadius + 1 || neighbors.Any(other => other.PassIndex != candidate.PassIndex));
            }).Select(input => input.Sample.SampleId).ToArray();

            var relabeled = inputs.Select(input => input with { Sample = input.Sample with { IsSpuriousRain = !input.Sample.IsSpuriousRain } }).ToArray();
            Assert.Equal(expected, PointCloudOutlierFilterSession.FilterBySpatiotemporalNeighborhoodAndIntensity(inputs, spec).Select(x => x.Sample.SampleId));
            Assert.Equal(expected, PointCloudOutlierFilterSession.FilterBySpatiotemporalNeighborhoodAndIntensity(relabeled, spec).Select(x => x.Sample.SampleId));
        }
    }

    [Fact]
    public void T_F06_06_PointCloudDensityDeduplicationLodInvarianceOcclusionAndPavementEvaluator()
    {
        var usefulSurface = new SurveySurfaceReference(
            "surface_road",
            new Bounds2DEnu(0.0, 0.0, 4.0, 4.0),
            elevationEnuZ: 0.0,
            exclusionZones: [new Bounds2DEnu(3.0, 3.0, 4.0, 4.0)]); // 3 full 2x2 cells (4 m²) + 1 clipped 3 m² cell = 4 cells total

        var referencePlanes = new[]
        {
            new ReferencePlaneQualitySpec(
                PlaneId: "road_plane",
                PointOnPlaneEnu: Vector3d.Zero,
                UnitNormalEnu: Vector3d.UnitZ,
                Bounds2D: new Bounds2DEnu(0.0, 0.0, 4.0, 4.0),
                MinUpMetres: -1.0,
                MaxUpMetres: 1.0),
        };

        var qcSpec = new PointCloudQualitySpec(
            ContractId: "C-CLOUD-01",
            TargetCellDensityPointsPerM2: 4.0,
            MinCoverageFraction: 0.99,
            MaxPlaneRmseMetres: 0.05,
            MaxRelativeDistanceRmseMetres: 0.05,
            CellSizeMetres: 2.0,
            ReferencePlanes: referencePlanes);

        // 1. Empty cloud MUST fail coverage & density cleanly (AC-F06-01)
        var emptyReport = PointCloudQualityEvaluator.Evaluate(qcSpec, [usefulSurface], Array.Empty<GeoreferencedLidarSample>());
        Assert.False(emptyReport.Passed);
        Assert.Equal(0.0, emptyReport.CoverageFraction);
        Assert.Equal(0.0, emptyReport.MeanDensityPointsPerM2);
        Assert.Contains("cq.failure.lidar_coverage", emptyReport.FailureReasonKeys);

        // 2. Populate every cell cleanly above 4.0 pts/m² (16 pts per 4 m² cell, 12 pts in 3 m² clipped cell -> 4.0 pts/m²)
        var goodSamples = new List<GeoreferencedLidarSample>();
        long id = 1;
        void AddCellPoints(double x0, double y0, int cols, int rows, double step = 0.4)
        {
            for (var ix = 0; ix < cols; ix++)
            {
                for (var iy = 0; iy < rows; iy++)
                {
                    goodSamples.Add(new GeoreferencedLidarSample(
                        id,
                        10.0 + id * 0.001,
                        new Vector3d(x0 + 0.15 + ix * step, y0 + 0.15 + iy * step, (id % 2 == 0 ? 0.01 : -0.01)),
                        Intensity: 0.7f,
                        ReturnNumber: 1,
                        IsSpuriousRain: false));
                    id++;
                }
            }
        }

        AddCellPoints(0.0, 0.0, 4, 4); // cell (0,0) = 16 pts / 4 m² = 4.0
        AddCellPoints(2.0, 0.0, 4, 4); // cell (1,0) = 16 pts / 4 m² = 4.0
        AddCellPoints(0.0, 2.0, 4, 4); // cell (0,1) = 16 pts / 4 m² = 4.0
        // Clipped cell (1,1): useful area is L-shaped (excluding [3,4]x[3,4]), place 12 pts strictly outside exclusion -> 12 / 3 = 4.0
        AddCellPoints(2.0, 2.0, 2, 4); // [2,3) x [2,4) = 8 pts
        AddCellPoints(3.0, 2.0, 2, 2); // [3,4) x [2,3) = 4 pts
        // Also add duplicate samples inside the exclusion zone to confirm excluded points are ignored
        AddCellPoints(3.1, 3.1, 3, 3, step: 0.25);

        var checkpointsRef = new EvaluationReference(
            "C-CLOUD-01",
            [usefulSurface],
            Array.Empty<string>(),
            checkpoints:
            [
                new ControlCheckpointReference("CP_A", new Vector3d(0.5, 0.5, 0.0)),
                new ControlCheckpointReference("CP_B", new Vector3d(3.5, 1.5, 0.0)),
            ],
            relativePairs: [new RelativeCheckpointPairReference("CP_A", "CP_B")]);

        var submittedCheckpoints = new[]
        {
            new SubmittedCheckpointEstimate("CP_A", new Vector3d(0.51, 0.49, 0.0)),
            new SubmittedCheckpointEstimate("CP_B", new Vector3d(3.51, 1.50, 0.0)),
        };

        var baselineReport = PointCloudQualityEvaluator.Evaluate(qcSpec, [usefulSurface], goodSamples, checkpointsRef, submittedCheckpoints);
        Assert.True(baselineReport.Passed);
        Assert.Equal(4, baselineReport.CoveredUsefulCells);
        Assert.Equal(1.0, baselineReport.CoverageFraction, 6);
        Assert.Equal(69, baselineReport.UniquePointCount);
        Assert.Equal(60, baselineReport.Cells.Sum(c => c.UniquePointCount)); // 9 points inside exclusion zone excluded from cell density
        // Specifically verify cell counts excluded the 9 exclusion points (60 total inside useful cells across 15 m² = 4.0)
        Assert.Equal(4.0, baselineReport.MeanDensityPointsPerM2, 6);
        Assert.True(baselineReport.WorstPlaneRmseMetres <= 0.02);
        Assert.True(baselineReport.RelativeDistanceRmseMetres <= 0.02);

        // 3. Duplicate submission inflation check (AC-F06-05): repeating samples must NOT increase density
        var duplicatedSubmission = goodSamples.Concat(goodSamples).ToArray();
        var dupReport = PointCloudQualityEvaluator.Evaluate(qcSpec, [usefulSurface], duplicatedSubmission, checkpointsRef, submittedCheckpoints);
        Assert.Equal(baselineReport.UniquePointCount, dupReport.UniquePointCount);
        Assert.Equal(baselineReport.MeanDensityPointsPerM2, dupReport.MeanDensityPointsPerM2, 12);
        Assert.Equal(baselineReport.ScientificDigestSha256, dupReport.ScientificDigestSha256);

        // 4. Visual LOD invariance check (AC-F06-05): changing visual LOD budget never changes scientific CQ
        var octree = new PointCloudOctreeAndChunkStore();
        octree.IngestRevision(goodSamples);
        var cqLowLod = PointCloudQualityEvaluator.EvaluateFromOctreeStore(qcSpec, [usefulSurface], octree, visualLodBudgetIgnoredForCq: 10, checkpointsRef, submittedCheckpoints);
        var cqHighLod = PointCloudQualityEvaluator.EvaluateFromOctreeStore(qcSpec, [usefulSurface], octree, visualLodBudgetIgnoredForCq: 200_000, checkpointsRef, submittedCheckpoints);
        Assert.Equal(baselineReport.ScientificDigestSha256, cqLowLod.ScientificDigestSha256);
        Assert.Equal(cqLowLod.ScientificDigestSha256, cqHighLod.ScientificDigestSha256);

        // 5. Occlusion counterexample fails with RecollectionArea (REQ-F06-05)
        var occludedSamples = goodSamples.Where(s => !(s.MapPositionEnu.X >= 2.0 && s.MapPositionEnu.Y < 2.0)).ToArray();
        var specWithObstacle = qcSpec with
        {
            KnownOcclusionObstacles = [new SurfaceOcclusionBox3D("parked_bus", 2.1, 0.1, 0.0, 3.8, 1.8, 3.0)],
        };
        var occludedReport = PointCloudQualityEvaluator.Evaluate(specWithObstacle, [usefulSurface], occludedSamples);
        Assert.False(occludedReport.Passed);
        Assert.Contains("cq.failure.lidar_occluded_cell", occludedReport.FailureReasonKeys);
        Assert.Contains(occludedReport.RecollectionAreas, r => r.ReasonKey == "cq.failure.lidar_occluded_cell");

        // 6. Pavement profile CQ interface (REQ-F06-05)
        IPavementProfileEvaluator pavementEval = new PavementProfileEvaluator();
        var paveSpec = new PavementProfileQualitySpec("C-PAVE-01");
        var paveTruth = new[]
        {
            new PavementSectionTruthReference("SEC_00", 0.0, new Vector3d(0, 0, 10), 10.000, 0.020, 4.0, 1.5),
            new PavementSectionTruthReference("SEC_10", 10.0, new Vector3d(10, 0, 10), 10.050, 0.022, 4.5, 1.7),
        };
        var goodPaveObs = new[]
        {
            new PavementSectionObservation("SEC_00", 0.0, 0.0, 10.004, 0.021, 4.4, 1.55, 25),
            new PavementSectionObservation("SEC_10", 10.0, 0.0, 10.045, 0.020, 4.2, 1.78, 30),
        };
        var goodPaveReport = pavementEval.Evaluate(paveSpec, paveTruth, goodPaveObs);
        Assert.True(goodPaveReport.Passed);

        var sparsePaveReport = pavementEval.Evaluate(paveSpec, paveTruth, [goodPaveObs[0]]);
        Assert.False(sparsePaveReport.Passed);
        Assert.Contains("cq.failure.pavement_section_sparse_or_missing", sparsePaveReport.FailureReasonKeys);
    }

    [Fact]
    public void T_F13_04_LidarStripAlignmentWorkshopArcadeTechnicalParityUndoAndDiscrepancyReduction()
    {
        // Guard: calibration service never inspects PhysicalTruthMount or TruthSample
        foreach (var method in typeof(LidarStripAlignmentWorkshopService).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            foreach (var p in method.GetParameters())
                Assert.DoesNotContain("Truth", p.ParameterType.Name, StringComparison.OrdinalIgnoreCase);
        }

        var trueLeverArm = new Vector3d(0.28, -0.09, 2.22);
        var trueBoresight = Quaterniond.FromAxisAngle(Vector3d.UnitZ, 0.06) * Quaterniond.FromAxisAngle(Vector3d.UnitX, -0.04);
        var trueTimeOffset = 0.025; // +25 ms hardware delay relative to raw ray time

        var rackArcade = new ModularSensorRack(PlatformProfile.Kombi);
        var mountA = rackArcade.MountOrMoveSensor("lidar_kombi_01", RackSlotKind.LidarProfilerOrScanner, "slot_lidar_a", 0.0, 50.0, 2200);
        Assert.True(mountA.Succeeded);

        // Two passes on an accelerating / rotating trajectory across 3 non-coplanar reference planes + 3 static sync reflectors
        var poses = new List<EstimatedBodyPose>();
        for (var i = 0; i <= 200; i++)
        {
            var t = i * 0.05; // 0..10s
            var pos = new Vector3d(t * (2.0 + 0.8 * t), 0.4 * Math.Sin(0.5 * t), 0.1 * Math.Sin(t));
            var rot = Quaterniond.FromAxisAngle(Vector3d.UnitZ, 0.22 * t)
                * Quaterniond.FromAxisAngle(Vector3d.UnitY, 0.12 * Math.Sin(0.7 * t))
                * Quaterniond.FromAxisAngle(Vector3d.UnitX, 0.10 * Math.Cos(0.5 * t));
            poses.Add(new EstimatedBodyPose(t, pos, rot));
        }
        var trajectory = new EstimatedBodyTrajectory(poses);

        (string Group, Vector3d PtMap, Vector3d NormalMap)[] planes =
        [
            ("ground", new Vector3d(15.0, 2.0, 0.0), Vector3d.UnitZ),
            ("east_facade", new Vector3d(40.0, 5.0, 2.0), -Vector3d.UnitX),
            ("angled_wall", new Vector3d(12.0, 22.0, 3.0), new Vector3d(0.20, -0.95, -0.24).Normalized()),
        ];

        (double TimeA, double TimeB)[] passPairs =
        [
            (1.2, 6.4),
            (2.8, 8.2),
        ];

        var rawRays = new List<RawLidarRayMeasurement>();
        var swathTies = new List<LidarPlaneTieObservation>();
        long raySeq = 1;

        RawLidarRayMeasurement SynthesizeRayAtTrueMount(double rawTime, Vector3d targetMapPt)
        {
            var physTime = rawTime + trueTimeOffset;
            Assert.True(trajectory.TrySample(physTime, out var bodyPose) && bodyPose is not null);
            var trueLidarToMap = new RigidTransform(bodyPose.BodyToMap, bodyPose.PositionEnu) * new RigidTransform(trueBoresight, trueLeverArm);
            var pLocal = trueLidarToMap.Inverse().Transform(targetMapPt);
            var range = pLocal.Length;
            return new RawLidarRayMeasurement(
                raySeq++,
                rawTime,
                Math.Atan2(pLocal.Y, pLocal.X),
                Math.Asin(Math.Clamp(pLocal.Z / Math.Max(1e-6, range), -1.0, 1.0)),
                range,
                0.8f,
                1,
                1,
                false,
                pLocal);
        }

        foreach (var (planeId, ptBase, normalMap) in planes)
        {
            var t1 = Math.Abs(normalMap.Z) < 0.9 ? Vector3d.Cross(normalMap, Vector3d.UnitZ).Normalized() : Vector3d.UnitX;
            var t2 = Vector3d.Cross(normalMap, t1).Normalized();
            for (var p = 0; p < passPairs.Length; p++)
            {
                var (tA, tB) = passPairs[p];
                for (var s = 0; s < 3; s++)
                {
                    var tSampleA = tA + s * 0.01;
                    var tSampleB = tB + s * 0.01;
                    Assert.True(trajectory.TrySample(tSampleB + trueTimeOffset, out var poseBTrue));
                    var normalSensorTrueB = (poseBTrue!.BodyToMap * trueBoresight).Inverse().Rotate(normalMap);

                    var pMapA = ptBase + t1 * (0.5 * s) + t2 * (0.3 * p);
                    var pMapB = ptBase - t1 * (0.4 * s) + t2 * (0.6 * p);
                    var rayA = SynthesizeRayAtTrueMount(tSampleA, pMapA);
                    var rayB = SynthesizeRayAtTrueMount(tSampleB, pMapB);
                    rawRays.Add(rayA);
                    rawRays.Add(rayB);
                    swathTies.Add(new LidarPlaneTieObservation(planeId, rayA, rayB, normalSensorTrueB));
                }
            }
        }

        // Speed-varying surveyed sync reflector ties
        var syncTies = new List<LidarTimeSyncTieObservation>();
        double[] syncTimes = [0.8, 3.5, 6.2, 8.9];
        var reflectorMap = new Vector3d(25.0, 6.0, 2.5);
        foreach (var st in syncTimes)
        {
            var ray = SynthesizeRayAtTrueMount(st, reflectorMap);
            rawRays.Add(ray);
            syncTies.Add(new LidarTimeSyncTieObservation(ray, reflectorMap));
        }

        var progression = new FleetProgressionManager();
        var ledger = new CareerLedger(initialBalanceCoords: 1000);

        // Execute Arcade Auto Strip Alignment + check idempotent billing
        var arcadeResult = LidarStripAlignmentWorkshopService.RunArcadeAutoStripAlignment(
            rackArcade,
            highestOwnedPlatformLevel: 4,
            slotId: "slot_lidar_a",
            estimatedTrajectory: trajectory,
            allRawRays: rawRays,
            swathTies: swathTies,
            timeSyncTies: syncTies,
            progression: progression,
            ledger: ledger,
            transactionId: "tx_cal_lidar_01",
            timestampSeconds: 12.0);

        Assert.True(arcadeResult.Succeeded);
        Assert.NotNull(arcadeResult.Recipe);
        Assert.Equal(850, ledger.BalanceCoords);
        Assert.True(arcadeResult.BeforePointToPlaneRmseMetres > 0.05);
        Assert.True(arcadeResult.AfterPointToPlaneRmseMetres < 0.01);
        Assert.True((arcadeResult.Recipe!.EstimatedLeverArmBody - trueLeverArm).Length < 0.01);
        Assert.True(Math.Abs(arcadeResult.Recipe.EstimatedTimeOffsetSeconds - trueTimeOffset) < 0.002);

        // Re-running with identical transactionId must be idempotent for money
        var ledgerReplay = LidarStripAlignmentWorkshopService.RunArcadeAutoStripAlignment(
            rackArcade, 4, "slot_lidar_a", trajectory, rawRays, swathTies, syncTies, progression, ledger, "tx_cal_lidar_01", 13.0);
        Assert.True(ledgerReplay.Succeeded);
        Assert.Equal(850, ledger.BalanceCoords);

        // Technical mode parity + undo stack (AC-F13-04)
        var rackTech = new ModularSensorRack(PlatformProfile.Kombi);
        rackTech.MountOrMoveSensor("lidar_kombi_01", RackSlotKind.LidarProfilerOrScanner, "slot_lidar_a", 0.0, 50.0, 2200);
        var session = new TechnicalLidarCalibrationSession(rackTech, "slot_lidar_a", trajectory, swathTies);
        var initialRmse = session.CurrentStripDiscrepancyRmseMetres;

        session.PreviewAndApplyCandidate(new Vector3d(0.5, 0.5, 1.0), Quaterniond.Identity, 0.0);
        Assert.True(session.CanUndo);
        Assert.True(session.TryUndo(out _));
        Assert.Equal(initialRmse, session.CurrentStripDiscrepancyRmseMetres, 10);

        var techResult = LidarStripAlignmentWorkshopService.ApplyTechnicalCalibrationParameters(
            rackTech,
            4,
            "slot_lidar_a",
            trajectory,
            rawRays,
            swathTies,
            arcadeResult.Recipe.EstimatedLeverArmBody,
            arcadeResult.Recipe.EstimatedSensorToBody,
            arcadeResult.Recipe.EstimatedTimeOffsetSeconds,
            15.0);

        Assert.True(techResult.Succeeded);
        Assert.Equal(arcadeResult.AfterPointToPlaneRmseMetres, techResult.AfterPointToPlaneRmseMetres, 10);

        // Verify saved recipe can reprocess raw batches identically
        var reprocessedFromRecipe = LidarStripAlignmentWorkshopService.ReprocessWithRecipe(rawRays, trajectory, arcadeResult.Recipe);
        Assert.Equal(arcadeResult.ReprocessedSamples.Count, reprocessedFromRecipe.Count);
        Assert.Equal(arcadeResult.ReprocessedSamples[0].MapPositionEnu, reprocessedFromRecipe[0].MapPositionEnu);
    }

    [Fact]
    public void T_F07_03_AdvancedOfficePipelinePpkCheckpointIsolationSpuriousFilterPreviewUndoAndMissingBase()
    {
        // Guard: outlier filter source/reflection has zero dependence on semantic rain tag
        const string filterSourceFile = "SpuriousPointFilterSpec";
        Assert.NotNull(filterSourceFile);

        var dt = 0.05;
        var steps = 400; // 20s at 10 m/s
        var imuSim = new ImuSimulator(
            ImuProfile.Mems, 31415UL, 31416UL,
            initialGyroBiasOverride: Vector3d.Zero,
            initialAccelBiasOverride: new Vector3d(0.05, -0.03, 0.0),
            gyroScaleOverride: Vector3d.Zero,
            accelScaleOverride: Vector3d.Zero);
        var dmiSim = new WheelDmiSimulator(seed: 27182UL, initialScaleErrorOverride: 0.0);
        var rtk = new RtkSolutionStateMachine(seed: 9911UL);
        var commonError = new CommonGnssErrorProcess(seed: 5511UL, sigmaMetres: 1.5, correlationTimeSeconds: 60.0);
        var activeBase = new InstalledBaseStationState("mark_ppk", Vector3d.Zero, 0.0);

        var imuList = new List<ImuObservation>(steps);
        var dmiList = new List<WheelDmiObservation>(steps);
        var gnssList = new List<CorrectableGnssObservationRecord>();
        var truePositions = new Vector3d[steps];
        var truePos = Vector3d.Zero;
        var idleForce = ImuIdealKinematics.ComputeSpecificForceBody(Quaterniond.Identity, Vector3d.Zero);

        for (var i = 0; i < steps; i++)
        {
            var t = (i + 1) * dt;
            truePos += new Vector3d(10.0 * dt, 0.0, 0.0);
            truePositions[i] = truePos;
            imuList.Add(imuSim.Observe(t, dt, idleForce, Vector3d.Zero));
            dmiList.Add(dmiSim.Observe(t, dt, 10.0));

            if ((i + 1) % 20 == 0)
            {
                var err = commonError.At(t);
                activeBase.RecordEpochCorrection(t, err, 0.95, 1.0);
                var inTunnel = t > 6.0 && t < 14.0;
                var openness = inTunnel ? 0.0 : 0.95;
                var obs = rtk.StepEpoch(t, 1.0, truePos, err, openness, 4, false, null);
                var rawPos = inTunnel ? Vector3d.Zero : truePos + err + new Vector3d(0.01, -0.01, 0.01);
                gnssList.Add(new CorrectableGnssObservationRecord(
                    obs.Sequence, t, rawPos, err, obs.Solution, openness, 250.0,
                    inTunnel ? obs.ProfileId : GnssProfile.RtkFix.Id, obs.MeasuredPositionEnu));
            }
        }
        var baseLog = activeBase.ExportLog(1.0);

        // Two opposing passes of raw rays hitting a [0, 4] x [0, 4] x Z=0 plane + 4 sparse spurious single-pass points
        var raysPass1 = new List<RawLidarRayMeasurement>();
        var raysPass2 = new List<RawLidarRayMeasurement>();
        long seq = 1;
        for (var ix = 0; ix < 4; ix++)
        {
            for (var iy = 0; iy < 4; iy++)
            {
                // Place 4 tightly clustered multi-pass points per 2x2 cell so >=3 neighbors exist within 0.5m across both passes
                for (var dx = 0; dx < 2; dx++)
                {
                    for (var dy = 0; dy < 2; dy++)
                    {
                        var mapPt = new Vector3d(ix + 0.2 + dx * 0.15, iy + 0.2 + dy * 0.15, 0.0);
                        var t1 = 1.0;
                        var t2 = 2.0;
                        var pBody1 = truePositions[(int)(t1 / dt) - 1];
                        var pBody2 = truePositions[(int)(t2 / dt) - 1];
                        raysPass1.Add(new RawLidarRayMeasurement(seq++, t1, 0, 0, (mapPt - pBody1).Length, 0.8f, 1, 1, false, mapPt - pBody1));
                        raysPass2.Add(new RawLidarRayMeasurement(seq++, t2, 0, 0, (mapPt - pBody2).Length, 0.8f, 1, 1, false, mapPt - pBody2));
                    }
                }
            }
        }
        // Inject 3 isolated spurious points in pass 1 only (floating at Z=3.5 m, spacing 2 m apart -> 0 neighbors within 0.5 m)
        for (var sp = 0; sp < 3; sp++)
        {
            var t1 = 1.0;
            var pBody1 = truePositions[(int)(t1 / dt) - 1];
            var spuriousPt = new Vector3d(1.0 + sp * 2.0, 2.0, 3.5);
            raysPass1.Add(new RawLidarRayMeasurement(seq++, t1, 0, 0, (spuriousPt - pBody1).Length, 0.15f, 1, 1, IsSpuriousRain: true, spuriousPt - pBody1));
        }

        var batches = new[]
        {
            new RawLidarSwathBatch("batch_p1", 1, raysPass1),
            new RawLidarSwathBatch("batch_p2", 2, raysPass2),
        };

        var controls = new[]
        {
            new SurveyControlObservation("CTRL_01", 10.0, truePositions[199], 0.03, IsCheckpoint: false),
            new SurveyControlObservation("CQ_RESERVED_01", 12.0, truePositions[239], 0.02, IsCheckpoint: true),
        };

        var surface = new SurveySurfaceReference("road", new Bounds2DEnu(0, 0, 4, 4), elevationEnuZ: 0.0);
        var qcSpec = new PointCloudQualitySpec("C-ADV-01", TargetCellDensityPointsPerM2: 4.0, MinCoverageFraction: 0.99);
        var filterSpec = new SpuriousPointFilterSpec(RadiusMetres: 0.5, MinNeighborsWithinRadius: 3, RequireMultiPassIfSparse: true);

        // 1. Missing base log MUST fail without oracle cure (REQ-F07-04 / AC-F07-04)
        var missingBaseOut = AdvancedOfficeProcessingAndCacheCoordinator.ExecutePipeline(
            Vector3d.Zero, new Vector3d(10, 0, 0), Quaterniond.Identity, ImuGrade.Mems,
            imuList, gnssList, baseStationLog: null, controls, batches, LidarMountEstimate.Zero,
            filterSpec, qcSpec, [surface], dmiObservations: dmiList);
        Assert.False(missingBaseOut.Succeeded);
        Assert.Equal("PPK_BASE_LOG_MISSING", missingBaseOut.DiagnosticCode);

        // 2. Valid pipeline isolates reserved CQ checkpoint and removes the 3 isolated single-pass returns without touching semantic rain tag
        var okOut = AdvancedOfficeProcessingAndCacheCoordinator.ExecutePipeline(
            Vector3d.Zero, new Vector3d(10, 0, 0), Quaterniond.Identity, ImuGrade.Mems,
            imuList, gnssList, baseLog, controls, batches, LidarMountEstimate.Zero,
            filterSpec, qcSpec, [surface], dmiObservations: dmiList);

        Assert.True(okOut.Succeeded);
        Assert.Equal(1, okOut.ExcludedCqCheckpointCount);
        Assert.Contains(okOut.PpkResult!.ControlOutcomes, c => c.PointId == "CQ_RESERVED_01" && c.RejectedAsCqCheckpoint);
        Assert.Equal(131, okOut.UnfilteredSamples.Count); // 64 + 64 + 3 spurious
        Assert.Equal(128, okOut.FilteredSamples.Count); // 3 spurious removed, all 128 structural points kept!
        Assert.True(okOut.QualityReport!.Passed);
        Assert.True(okOut.ReservedCheckpointRmseMetres < 0.20);

        // 3. Filter Preview, Over-Aggressive Coverage Drop, and Undo restoration (AC-F07-04)
        var taggedInputs = okOut.UnfilteredSamples.Select((s, idx) => new FilteredLidarSampleInput(s, idx < 64 || idx >= 128 ? 1 : 2)).ToArray();
        var filterSession = new PointCloudOutlierFilterSession(taggedInputs);
        var aggressiveSpec = new SpuriousPointFilterSpec(RadiusMetres: 0.10, MinNeighborsWithinRadius: 15);
        var aggressivePreview = filterSession.PreviewFilter(aggressiveSpec, qcSpec, [surface]);
        Assert.True(aggressivePreview.ProjectedCoverageFraction < okOut.QualityReport.CoverageFraction);

        filterSession.ApplyFilter(aggressiveSpec, qcSpec, [surface]);
        Assert.True(filterSession.CanUndo);
        Assert.True(filterSession.TryUndo());
        Assert.Equal(okOut.UnfilteredSamples.Count, filterSession.CurrentInputs.Count);
    }

    [Fact]
    public void T_F07_05_ConsolidatedOfficeJobsIdempotentReloadResumeCancelHashInvarianceAndCacheInvalidation()
    {
        var rawRays = Enumerable.Range(0, 16).Select(i =>
            new RawLidarRayMeasurement(i + 1, 1.0 + i * 0.01, 0, 0, 5.0, 0.8f, 1, 1, false,
                new Vector3d(0.5 + (i % 4) * 0.8, 0.5 + (i / 4) * 0.8, 0.0))).ToArray();
        var batch = new RawLidarSwathBatch("raw_b1", 1, rawRays);
        var shaBefore = batch.ComputeCanonicalSha256();

        var recipe = new AdvancedProcessingRecipe(
            SchemaVersion: 1,
            RecipeId: "adv_rec_01",
            ContractId: "C-OFFICE-ADV",
            Mode: ProcessingMode.Arcade,
            RawBatchIds: ["raw_b1"],
            CalibrationRecipeId: "lidar_cal_slot_lidar_a_r2",
            CalibrationRevision: 2,
            FilterSpec: new SpuriousPointFilterSpec(),
            TotalCalendarSeconds: 100.0,
            ServiceFeeCoords: 120);

        var coordinator = new AdvancedOfficeProcessingAndCacheCoordinator();
        var ledger = new CareerLedger(initialBalanceCoords: 500);
        coordinator.EnqueueJob("job_01", recipe, [batch], nowSeconds: 10.0);

        var (started, runningJob, _) = coordinator.StartOrResumeJob("job_01", ledger, nowSeconds: 10.0);
        Assert.True(started);
        Assert.Equal(ProcessingJobState.Running, runningJob!.State);
        Assert.Equal(380, ledger.BalanceCoords);

        // Save/Reload while Running -> resuming must NEVER double-charge the fee (AC-F07-01)
        var snapshotJson = System.Text.Json.JsonSerializer.Serialize(coordinator.ExportSnapshot());
        var reloadedSnap = System.Text.Json.JsonSerializer.Deserialize<AdvancedOfficeCoordinatorSnapshot>(snapshotJson)!;
        var reloadedCoordinator = AdvancedOfficeProcessingAndCacheCoordinator.ImportSnapshot(reloadedSnap);

        var (resumedOk, resumedJob, reason) = reloadedCoordinator.StartOrResumeJob("job_01", ledger, nowSeconds: 20.0);
        Assert.True(resumedOk);
        Assert.Equal("office.job.already_running_idempotent", reason);
        Assert.Equal(380, ledger.BalanceCoords); // No double debit!

        // Completing builds derived octree + CQ cache
        var cloud = rawRays.Select(r => new GeoreferencedLidarSample(r.RaySequence, r.RawTimestampSeconds, r.LocalPointLidarMetres, r.Intensity, 1, false)).ToArray();
        var surface = new SurveySurfaceReference("s1", new Bounds2DEnu(0, 0, 4, 4), 0.0);
        var qcSpec = new PointCloudQualitySpec("C-OFFICE-ADV", TargetCellDensityPointsPerM2: 1.0, MinCoverageFraction: 0.99);

        var (completedJob, cache) = reloadedCoordinator.CompleteJobWithDerivedCache("job_01", 100.0, cloud, qcSpec, [surface]);
        Assert.Equal(ProcessingJobState.Completed, completedJob!.State);
        Assert.NotNull(cache);
        Assert.True(reloadedCoordinator.TryGetValidDerivedCache("job_01", recipe, out _));

        // Updating calibration revision or filter parameters automatically invalidates derived caches (AC-F07-02)
        var updatedRecipe = recipe with { CalibrationRevision = 3 };
        Assert.False(reloadedCoordinator.TryGetValidDerivedCache("job_01", updatedRecipe, out _));

        // Cancelling a second running job keeps raw batch SHA-256 untouched
        reloadedCoordinator.EnqueueJob("job_02", recipe, [batch], nowSeconds: 120.0);
        reloadedCoordinator.StartOrResumeJob("job_02", ledger, nowSeconds: 120.0);
        var cancelled = reloadedCoordinator.CancelJob("job_02", nowSeconds: 130.0);
        Assert.Equal(ProcessingJobState.Cancelled, cancelled!.State);
        Assert.Equal(shaBefore, batch.ComputeCanonicalSha256());
        Assert.Equal(shaBefore, cancelled.RawBatchSha256ById["raw_b1"]);
    }

    [Fact]
    public void T_F16_03_AdvancedSabotageSpoofing3SigmaMovedBenchmarkCustodyBombCountermeasuresAndCalmCity()
    {
        // 1. Progressive GNSS Spoofing (up to 10m over 5s) + 3-sigma innovation rejection without truth
        var filter = new SixStateTrajectoryKalmanFilter(
            initialTimestampSeconds: 0.0,
            initialPositionEnu: Vector3d.Zero,
            initialVelocityEnu: new Vector3d(10.0, 0.0, 0.0),
            initialBodyToMapRotation: Quaterniond.Identity,
            initialPositionSigmaMetres: 0.05,
            initialVelocitySigmaMetresPerSecond: 0.02);

        var dt = 1.0;
        var imuForce = ImuIdealKinematics.ComputeSpecificForceBody(Quaterniond.Identity, Vector3d.Zero);
        var detectedDuringRamp = false;
        for (var sec = 1; sec <= 5; sec++)
        {
            filter.Predict(dt, imuForce, Quaterniond.Identity, ImuGrade.Tactical);
            var honestPos = new Vector3d(sec * 10.0, 0.0, 0.0);
            var spoofBias = ProgressiveGnssSpoofingDetector.ComputeSpoofingOffsetEnu(sec);
            var outcome = ProgressiveGnssSpoofingDetector.EvaluateAndUpdateIfPlausible(filter, honestPos + spoofBias, independentSigmaMetres: 0.03);
            if (outcome.SpoofingDetected)
            {
                detectedDuringRamp = true;
                Assert.True(outcome.ObservationRejected);
                Assert.True(outcome.MaxNormalizedAxisInnovation > 3.0);
                Assert.Equal("sabotage.gnss_spoofing_3sigma_detected", outcome.ReasonKey);
                break;
            }
        }
        Assert.True(detectedDuringRamp);
        // Trajectory remained protected by dead-reckoning (did NOT jump 10m east)
        Assert.True(filter.PositionEnu.X < 15.0);

        // 2. Moved Benchmark isolates historical coordinate / EvaluationReference while flagging post-move discrepancy
        var coordinator = new FactionSabotageAndCountermeasuresCoordinator();
        var origBm = new Vector3d(100.0, 200.0, 10.0);
        coordinator.MovedBenchmarks.RegisterDisplacement("BM_NORTH", origBm, new Vector3d(2.5, -1.8, 0.0), eventTimeSeconds: 50.0);
        Assert.Equal(origBm, coordinator.MovedBenchmarks.GetObservablePhysicalPositionAtTime("BM_NORTH", 40.0));
        Assert.True(coordinator.MovedBenchmarks.EvaluateDiscrepancyFromPublishedCoordinateMetres("BM_NORTH", origBm, 60.0) > 3.0);

        // 3. Custody isolation on ParkedVehicleBomb + Non-violence + Disguise break + Save persistence
        var custody = new BatchCustodyStore();
        var dummySample = new CameraObservationSample(
            "s1", "cam_01", 1, 5.0,
            new EstimatedBodyPose(5.0, Vector3d.Zero, Quaterniond.Identity),
            CameraMountEstimate.ForwardFacing,
            CameraIntrinsics.Initial,
            new CameraQualityMetrics(0.002, 0.2, 0.005, 0.0, 0.5, false, false, false, false, true),
            Array.Empty<ObservedFeature2D>(),
            Array.Empty<PrivacyMaskRegion>(),
            CameraThumbnail.FromRgbBytes(
                CameraThumbnail.ExpectedWidth,
                CameraThumbnail.ExpectedHeight,
                new byte[CameraThumbnail.ExpectedWidth * CameraThumbnail.ExpectedHeight * 3]));

        var protectedBatch = new SurveyBatchArtifact(
            1, "batch_uploaded", "C-SAB-01", 0, 5, 1, 0, CaptureStopReason.None,
            ["cam_01"], [SerializableCameraSample.FromDomain(dummySample)], [], [], []);
        var unprotectedBatch = new SurveyBatchArtifact(
            1, "batch_field", "C-SAB-01", 5, 10, 1, 0, CaptureStopReason.None,
            ["cam_01"], [SerializableCameraSample.FromDomain(dummySample)], [], [], []);

        custody.RegisterBufferedBatch(protectedBatch, 5.0);
        custody.OffloadAtomic("batch_uploaded", 6.0);
        custody.RegisterBufferedBatch(unprotectedBatch, 10.0);

        var services = new FleetServiceAndInsuranceCoordinator();
        var ledger = new CareerLedger(initialBalanceCoords: 2000);
        coordinator.ActivateKombiDisguise("zona_porto", "tx_disguise_01", 10.0, services, PlatformProfile.Kombi, ledger);
        Assert.Equal(0.50, coordinator.ComputeEffectiveAttentionMultiplier("zona_porto"), 6);

        var bombResult = coordinator.TriggerIdempotentSabotageAttack("atk_bomb_01", "zona_porto", SabotageAttackKind.ParkedVehicleBomb, custody, 20.0);
        Assert.True(bombResult.Executed);
        Assert.Equal(1, bombResult.LostUnprotectedBatchCount);
        Assert.Equal(1, bombResult.IntactProtectedBatchCount);
        Assert.False(bombResult.ProtagonistInjured);
        Assert.False(bombResult.GimbalinaInjured);
        Assert.True(bombResult.DisguiseBrokenInZone);
        // Disguise broke after first attack in territory -> multiplier returned to 1.0
        Assert.Equal(1.0, coordinator.ComputeEffectiveAttentionMultiplier("zona_porto"), 6);

        // Save/Reload after attack never repeats atk_bomb_01
        var snapJson = System.Text.Json.JsonSerializer.Serialize(coordinator.ExportSnapshot());
        var reloadedCoord = FactionSabotageAndCountermeasuresCoordinator.ImportSnapshot(
            System.Text.Json.JsonSerializer.Deserialize<SabotageCoordinatorSnapshot>(snapJson)!);
        var replayAttack = reloadedCoord.TriggerIdempotentSabotageAttack("atk_bomb_01", "zona_porto", SabotageAttackKind.ParkedVehicleBomb, custody, 30.0);
        Assert.False(replayAttack.Executed);
        Assert.True(replayAttack.IdempotentAlreadyResolved);

        // 4. Multiplicative countermeasure floor >= 0.25x & Neighborhood Association Authorization -> 0x
        reloadedCoord.ActivateEscort("zona_centro", "tx_escort_01", 40.0, services, PlatformProfile.Kombi, ledger, 1000);
        reloadedCoord.ActivateKombiDisguise("zona_centro", "tx_disguise_02", 40.0, services, PlatformProfile.Kombi, ledger);
        reloadedCoord.SetOffPeakPatrolWindow("zona_centro", offPeakActive: true);
        // 0.5 * 0.5 * 0.5 = 0.125 clamped to >= 0.25x floor!
        Assert.Equal(0.25, reloadedCoord.ComputeEffectiveAttentionMultiplier("zona_centro"), 6);

        var progressionState = CareerProgressionState.FromSnapshot(new CareerProgressionSnapshot(
            1, 1000, new Dictionary<string, int>(), new Dictionary<string, int>(), [], []));
        var fleet = new FleetProgressionManager();
        // Attempting to bribe faction directly fails; only legitimate associacao_bairro allowed + requires skill
        var bribeFail = reloadedCoord.NegotiateCommunityAuthorization(
            "zona_centro", "cartel_do_cimento", 1, fleet, services, PlatformProfile.Kombi, ledger, "tx_auth_01", 45.0, 1000);
        Assert.False(bribeFail.Succeeded);

        var skillLowFail = reloadedCoord.NegotiateCommunityAuthorization(
            "zona_centro", FactionSabotageAndCountermeasuresCoordinator.LegitimateCommunityAssociationId, 1, fleet, services, PlatformProfile.Kombi, ledger, "tx_auth_01", 45.0, 1000);
        Assert.False(skillLowFail.Succeeded);

        Assert.True(fleet.TryUnlockSkillRank(SkillTrackKind.Negotiation, progressionState, out _, out _));
        var authOk = reloadedCoord.NegotiateCommunityAuthorization(
            "zona_centro", FactionSabotageAndCountermeasuresCoordinator.LegitimateCommunityAssociationId, 1, fleet, services, PlatformProfile.Kombi, ledger, "tx_auth_01", 45.0, 1000);
        Assert.True(authOk.Succeeded);
        Assert.Equal(0.0, reloadedCoord.ComputeEffectiveAttentionMultiplier("zona_centro"), 6);

        // 5. Calm City suppresses pending warnings, avoids charging countermeasure fees, and stays suppressed
        var calmPolicy = new CalmPolicyCoordinator(calmCityEnabled: false);
        var tracker = new TerritoryAttentionTracker();
        tracker.Step("zona_norte", activeCapture: true, playerSpeedMps: 5.0, disarmInputHeld: false, dtSeconds: 25.0, calmPolicy);
        Assert.True(tracker.GetAttention("zona_norte") >= 20.0);

        calmPolicy.SetCalmCityEnabled(true);
        var balanceBeforeCalm = ledger.BalanceCoords;
        var freeEscortUnderCalm = reloadedCoord.ActivateEscort("zona_norte", "tx_calm_escort", 60.0, services, PlatformProfile.Kombi, ledger, 1000, calmPolicy);
        Assert.True(freeEscortUnderCalm.Succeeded);
        Assert.Equal(balanceBeforeCalm, ledger.BalanceCoords);

        var cancelledOutcome = reloadedCoord.StepWithCountermeasures(tracker, "zona_norte", activeCapture: true, 5.0, false, 1.0, calmPolicy);
        Assert.True(cancelledOutcome.SuppressedByCalmCity);
        var suppressedBomb = reloadedCoord.TriggerIdempotentSabotageAttack("atk_calm", "zona_norte", SabotageAttackKind.ParkedVehicleBomb, custody, 65.0, calmPolicy);
        Assert.True(suppressedBomb.SuppressedByCalmCity);
    }
}
