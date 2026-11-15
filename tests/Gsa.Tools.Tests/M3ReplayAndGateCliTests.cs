using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;
using Gsa.Tools;
using Gsa.Tools.Replay;
using Xunit;

namespace Gsa.Tools.Tests;

public sealed class M3ReplayAndGateCliTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gsa.Domain.slnf")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    public void ReplayCoreQualityReportAndRunM3GateCliCommandsExecuteEndToEnd()
    {
        var root = FindRepoRoot();
        var tempDir = Path.Combine(Path.GetTempPath(), $"gsa-m3-cli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var contractPath = Path.Combine(root, "content", "contracts", "hist-02-drainage.json");
            var contractJson = File.ReadAllText(contractPath);
            var def = ContractDefinition.Parse(contractJson, "hist-02-drainage.json");
            var rgb = new byte[CameraThumbnail.ExpectedWidth * CameraThumbnail.ExpectedHeight * 3];
            var thumb = CameraThumbnail.FromRgbBytes(CameraThumbnail.ExpectedWidth, CameraThumbnail.ExpectedHeight, rgb);

            var samples = new List<SerializableCameraSample>(def.RequiredFeatureIds.Count);
            var footprints = new List<SampleFootprintObservation>(def.RequiredFeatureIds.Count);
            var features = new List<TriangulatedFeatureEntry>(def.RequiredFeatureIds.Count);
            var featLocs = new Dictionary<string, Vector3d>(StringComparer.Ordinal);
            var bounds = new Bounds2DEnu(500008.0, 7400008.0, 500232.0, 7400052.0);

            for (var i = 0; i < def.RequiredFeatureIds.Count; i++)
            {
                var fid = def.RequiredFeatureIds[i];
                var pos = new Vector3d(500020.0 + i * 16.0, 7400025.0, 100.5);
                featLocs[fid] = pos;
                var sid = $"cli_s_{i + 1:00}";
                var s = new CameraObservationSample(
                    sid, "cam_initial", i, 10.0 + i * 5.0,
                    new EstimatedBodyPose(10.0 + i * 5.0, pos + new Vector3d(-2.0, 0.0, 0.5), Quaterniond.Identity),
                    CameraMountEstimate.ForwardFacing, CameraIntrinsics.Initial,
                    new CameraQualityMetrics(0.002, 0.25, 0.005, 0.0, 0.35, false, false, false, false, true), [], [], thumb);
                samples.Add(SerializableCameraSample.FromDomain(s));
                footprints.Add(new SampleFootprintObservation(sid, "commerce_drainage_surface", bounds));
                features.Add(new TriangulatedFeatureEntry(
                    fid, "manhole", TriangulationStatus.Resolved, FeatureReconstructionMethod.TwoRayTriangulation,
                    GeometryFailureReason.None, pos, 4.0, null, 2, true, [sid]));
            }

            ControlCheckpointReference[] checkpoints =
            [
                new("cli_chk_01", new Vector3d(500020.0, 7400020.0, 100.5)),
                new("cli_chk_02", new Vector3d(500110.0, 7400020.0, 100.5)),
                new("cli_chk_03", new Vector3d(500200.0, 7400020.0, 100.5)),
            ];
            var submittedCheckpoints = checkpoints.Select(c => new SubmittedCheckpointEstimate(c.CheckpointId, c.TruePositionEnu)).ToArray();

            var batch = new SurveyBatchArtifact(
                1, "batch_cli_rep", def.Id, 10.0, 70.0, samples.Count, 0, CaptureStopReason.None,
                ["cam_initial"], samples, footprints, features, submittedCheckpoints);
            var evalRef = new EvaluationReference(
                def.Id, [new SurveySurfaceReference("commerce_drainage_surface", bounds, 100.5)],
                def.RequiredFeatureIds, checkpoints, [new RelativeCheckpointPairReference("cli_chk_01", "cli_chk_02")], featLocs);

            var cm = new ContractStateMachine(def);
            cm.Accept(0.0);
            cm.RecordPendingBatch(70.0, batch.BatchId);
            var store = new BatchCustodyStore();
            store.RegisterBufferedBatch(batch, 70.0);
            store.OffloadAtomic(batch.BatchId, 72.0);
            cm.ProtectBatchAtUnloadPoint(72.0, batch.BatchId);
            var queue = new OfficeProcessingQueue();
            var ledger = new CareerLedger(1000);
            var recipe = SimplifiedOfficeWorkstation.BuildArcadeAssistedRecipe("rec_cli_rep", cm, store, ledger, sourceBatchIds: [batch.BatchId]) with { CalendarDurationSeconds = 0.0 };
            var product = queue.StartOrResumeJob("job_cli_rep", cm, store, ledger, recipe, 75.0).Product!;
            var report = QualityEvaluator.Evaluate(def, evalRef, product.Submission);
            var recordedReportJson = report.ToJson();
            var recordedSha = ScientificReplayEngine.ComputeCanonicalSha256(recordedReportJson);

            var pkg = new ReplayPackage(
                1, ReplayAndQualityCliTool.CurrentBuildVersion, ReplayAndQualityCliTool.CurrentModelVersion,
                ReplayAndQualityCliTool.DefaultOldCenterMapSha256, ["cam_initial"], 42UL, contractJson,
                batch, recipe, ReplayEvaluationReferenceDto.FromDomain(evalRef),
                [new VisualPoseKeyframe(0.0, new Vector3d(500010.0, 7400025.0, 100.5), Quaterniond.Identity, 1.5)],
                [], recordedReportJson, recordedSha);

            var replayJsonPath = Path.Combine(tempDir, "replay.json");
            var reportAPath = Path.Combine(tempDir, "report-a.json");
            var reportBPath = Path.Combine(tempDir, "report-b.json");
            var compareOutPath = Path.Combine(tempDir, "comparison.json");
            var m3GateOutPath = Path.Combine(tempDir, "m3-gate.json");

            File.WriteAllText(replayJsonPath, JsonSerializer.Serialize(pkg));
            File.WriteAllText(reportAPath, recordedReportJson);

            using var out1 = new StringWriter();
            Assert.Equal(0, ToolApplication.Run(["replay-core", replayJsonPath, reportBPath], out1));
            Assert.True(File.Exists(reportBPath));

            using var out2 = new StringWriter();
            Assert.Equal(0, ToolApplication.Run(["quality-report", reportAPath, reportBPath, compareOutPath], out2));
            using var cmpDoc = JsonDocument.Parse(out2.ToString());
            Assert.True(cmpDoc.RootElement.GetProperty("equivalent").GetBoolean());

            using var out3 = new StringWriter();
            var exit3 = ToolApplication.Run([
                "run-m3-gate",
                Path.Combine(root, "content"),
                Path.Combine(root, "game", "world", "tutorial-block.json"),
                Path.Combine(root, "game", "localization", "en_US.po"),
                Path.Combine(root, "game", "localization", "pt_BR.po"),
                Path.Combine(root, "content", "localization", "glossary.csv"),
                m3GateOutPath,
            ], out3);
            Assert.Equal(0, exit3);
            using var m3Doc = JsonDocument.Parse(out3.ToString());
            Assert.True(m3Doc.RootElement.GetProperty("valid").GetBoolean());
            Assert.Equal(3, m3Doc.RootElement.GetProperty("missions_passed").GetInt32());
            Assert.Equal(500, m3Doc.RootElement.GetProperty("procedural_seeds_passed").GetInt32());
            Assert.Equal(16, m3Doc.RootElement.GetProperty("tasks_linked").GetInt32());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
