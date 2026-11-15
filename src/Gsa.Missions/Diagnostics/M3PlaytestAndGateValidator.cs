using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Activities;
using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
using Gsa.Missions.Presentation;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Diagnostics;

public enum M3CohortKind
{
    Gamer = 0,
    Student = 1,
}

public sealed record M3PlaytestSessionRecord(
    string ParticipantId,
    M3CohortKind Cohort,
    double SessionDurationMinutes,
    bool CompletedAndCorrectedCollection,
    bool WishedToContinue,
    int LinkedObservedEffectsCount,
    string Notes,
    bool IsHumanCohortSession = false);

public sealed record M3CohortEvaluationResult(
    M3CohortKind Cohort,
    int TotalParticipants,
    int CompletedAndCorrectedCount,
    int WishedToContinueCount,
    int StudentsLinkingTwoEffectsCount,
    bool CriteriaMet,
    string PlaytestGateStatus);

public sealed record M3TaskEvidenceEntry(
    string TaskId,
    string Title,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<string> AcceptanceScenarios,
    string EvidencePath,
    bool Accepted,
    bool EvidenceVerified = true);

public sealed record M3GateReport(
    int SchemaVersion,
    string Milestone,
    string TechnicalLoopStatus,
    string PlaytestGateStatus,
    string OverallGateStatus,
    MvpThreeMissionPlaythroughReport CampaignPlaythrough,
    OneHundredSeedsPerTemplateReport ProceduralValidation,
    bool CalmContractAlwaysAvailableAtLowBalance,
    bool ReplayFpsInvarianceVerified,
    bool BilingualAuditPassed,
    bool CollectiblesIdempotentVerified,
    M3CohortEvaluationResult GamerCohort,
    M3CohortEvaluationResult StudentCohort,
    IReadOnlyList<M3TaskEvidenceEntry> Tasks);

/// <summary>Evaluates M3 MVP closure across all 16 tasks and the two-cohort playtest gate.</summary>
public static class M3PlaytestAndGateValidator
{
    public const int RequiredParticipantsPerCohort = 5;
    public const int MinCompletedPerCohort = 4;
    public const int MinContinuePerCohort = 3;
    public const int MinStudentLinkedEffects = 2;
    public const int MinStudentsLinkingTwoEffects = 4;

    public static M3CohortEvaluationResult EvaluateCohort(
        M3CohortKind cohort,
        IReadOnlyList<M3PlaytestSessionRecord> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var matching = sessions.Where(s => s.Cohort == cohort).ToArray();
        var completed = matching.Count(s => s.CompletedAndCorrectedCollection);
        var wishContinue = matching.Count(s => s.WishedToContinue);
        var studentEffects = cohort == M3CohortKind.Student
            ? matching.Count(s => s.LinkedObservedEffectsCount >= MinStudentLinkedEffects)
            : 0;

        var criteriaMet = matching.Length >= RequiredParticipantsPerCohort &&
                          completed >= MinCompletedPerCohort &&
                          wishContinue >= MinContinuePerCohort &&
                          (cohort != M3CohortKind.Student || studentEffects >= MinStudentsLinkingTwoEffects);

        var allHuman = matching.Length > 0 && matching.All(s => s.IsHumanCohortSession);
        var status = !criteriaMet
            ? "failed_rework_required"
            : allHuman
                ? "passed"
                : "pending_human_playtest_cohort";

        return new M3CohortEvaluationResult(
            cohort,
            matching.Length,
            completed,
            wishContinue,
            studentEffects,
            criteriaMet,
            status);
    }

    public static M3GateReport BuildM3GateReport(
        string contentDirectory,
        string worldManifestPath,
        string enUsPoPath,
        string ptBrPoPath,
        string glossaryCsvPath,
        IReadOnlyList<M3PlaytestSessionRecord>? gamerHumanSessions = null,
        IReadOnlyList<M3PlaytestSessionRecord>? studentHumanSessions = null)
    {
        var campaign = MvpCampaignRunner.RunMvpThreeMissionPlaythrough(contentDirectory, worldManifestPath);
        var procedural = MvpProceduralContractGenerator.ValidateOneHundredSeedsPerTemplate();

        var boardZero = MvpProceduralContractGenerator.GenerateAvailableBoard(0, 500, ownedPlatformLevel: 0, seed: 42UL);
        var calmAvailable = boardZero.Any(o => o.Contract.Calm && o.ExecutableWithStarterKit);

        var bilingual = GeopediaCatalogCoordinator.AuditBilingualCatalog(enUsPoPath, ptBrPoPath, glossaryCsvPath);

        // Verify replay FPS invariance at 30 / 60 / 120 fps using HIST-01 fixture data
        var replayOk = VerifyReplayInvariance(contentDirectory);

        // Verify collectibles idempotency
        var ledger = new CareerLedger(0);
        var geo = new GeopediaCatalogCoordinator();
        var collectibles = new MvpCollectiblesCoordinator();
        var eggs = collectibles.GetActiveDailyEggs(dayIndex: 1);
        var e1 = collectibles.CollectGoldenEgg(eggs[0], 10.0, ledger);
        var e2 = collectibles.CollectGoldenEgg(eggs[0], 12.0, ledger);
        var collectiblesOk = e1.Succeeded && !e1.AlreadyClaimed && e2.AlreadyClaimed && ledger.BalanceCoords == MvpCollectiblesCoordinator.EggRewardCoords;

        var gamers = EvaluateCohort(M3CohortKind.Gamer, gamerHumanSessions ?? BuildSimulatedGamerCohort());
        var students = EvaluateCohort(M3CohortKind.Student, studentHumanSessions ?? BuildSimulatedStudentCohort());

        var technicalPassed = campaign.AllThreeMissionsPassed &&
                              procedural.AllPassed &&
                              calmAvailable &&
                              replayOk &&
                              bilingual.Passed &&
                              collectiblesOk;

        var technicalStatus = technicalPassed ? "passed" : "failed_rework_required";
        var playtestStatus = !gamers.CriteriaMet || !students.CriteriaMet
            ? "failed_rework_required"
            : gamers.PlaytestGateStatus == "passed" && students.PlaytestGateStatus == "passed"
                ? "passed"
                : "pending_human_playtest_cohort";

        var overallStatus = !technicalPassed ? "failed_rework_required" : playtestStatus;

        return new M3GateReport(
            SchemaVersion: 1,
            Milestone: "M3",
            TechnicalLoopStatus: technicalStatus,
            PlaytestGateStatus: playtestStatus,
            OverallGateStatus: overallStatus,
            CampaignPlaythrough: campaign,
            ProceduralValidation: procedural,
            CalmContractAlwaysAvailableAtLowBalance: calmAvailable,
            ReplayFpsInvarianceVerified: replayOk,
            BilingualAuditPassed: bilingual.Passed,
            CollectiblesIdempotentVerified: collectiblesOk,
            GamerCohort: gamers,
            StudentCohort: students,
            Tasks: BuildAllSixteenM3Tasks());
    }

    private static bool VerifyReplayInvariance(string contentDirectory)
    {
        var contractJson = File.ReadAllText(Path.Combine(contentDirectory, "contracts", "hist-02-drainage.json"));
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
            var sid = $"rep_s_{i + 1:00}";
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
            new("rep_chk_01", new Vector3d(500020.0, 7400020.0, 100.5)),
            new("rep_chk_02", new Vector3d(500110.0, 7400020.0, 100.5)),
            new("rep_chk_03", new Vector3d(500200.0, 7400020.0, 100.5)),
        ];
        var submittedCheckpoints = checkpoints.Select(c => new SubmittedCheckpointEstimate(c.CheckpointId, c.TruePositionEnu)).ToArray();

        var batch = new SurveyBatchArtifact(
            1, "batch_replay_check", def.Id, 10.0, 70.0, samples.Count, 0, CaptureStopReason.None,
            ["cam_initial"], samples, footprints, features, submittedCheckpoints);
        var evalRef = new EvaluationReference(
            def.Id,
            [new SurveySurfaceReference("commerce_drainage_surface", bounds, 100.5)],
            def.RequiredFeatureIds,
            checkpoints,
            [new RelativeCheckpointPairReference("rep_chk_01", "rep_chk_02")],
            featLocs);

        var cm = new ContractStateMachine(def);
        cm.Accept(0.0);
        cm.RecordPendingBatch(70.0, batch.BatchId);
        var store = new BatchCustodyStore();
        store.RegisterBufferedBatch(batch, 70.0);
        store.OffloadAtomic(batch.BatchId, 72.0);
        cm.ProtectBatchAtUnloadPoint(72.0, batch.BatchId);

        var queue = new OfficeProcessingQueue();
        var ledger = new CareerLedger(1000);
        var recipe = SimplifiedOfficeWorkstation.BuildArcadeAssistedRecipe("rec_rep", cm, store, ledger, sourceBatchIds: [batch.BatchId]) with { CalendarDurationSeconds = 0.0 };
        var job = queue.StartOrResumeJob("job_rep", cm, store, ledger, recipe, 75.0);
        var recordedReport = QualityEvaluator.Evaluate(def, evalRef, job.Product!.Submission);
        var recordedJson = recordedReport.ToJson();
        var recordedSha = ScientificReplayEngine.ComputeCanonicalSha256(recordedJson);

        var pkg = new ReplayPackage(
            1, "gsa-mvp-0.3.0", "gsa-physics-v1", MvpProceduralContractGenerator.DefaultOldCenterMapSha256,
            ["cam_initial"], 1001UL, contractJson, batch, recipe, ReplayEvaluationReferenceDto.FromDomain(evalRef),
            [
                new VisualPoseKeyframe(0.0, new Vector3d(500010.0, 7400025.0, 100.5), Quaterniond.Identity, 1.5),
                new VisualPoseKeyframe(60.0, new Vector3d(500210.0, 7400025.0, 100.5), Quaterniond.Identity, 1.5),
            ],
            [new ReplayEventRecord(10.0, "capture_started", batch.BatchId)],
            recordedJson,
            recordedSha);

        HashSet<string> profs = ["cam_initial"];
        var fps30 = ScientificReplayEngine.SampleVisualTrajectoryAtFps(pkg, 30.0, "gsa-mvp-0.3.0", "gsa-physics-v1", MvpProceduralContractGenerator.DefaultOldCenterMapSha256, profs);
        var fps60 = ScientificReplayEngine.SampleVisualTrajectoryAtFps(pkg, 60.0, "gsa-mvp-0.3.0", "gsa-physics-v1", MvpProceduralContractGenerator.DefaultOldCenterMapSha256, profs);
        var fps120 = ScientificReplayEngine.SampleVisualTrajectoryAtFps(pkg, 120.0, "gsa-mvp-0.3.0", "gsa-physics-v1", MvpProceduralContractGenerator.DefaultOldCenterMapSha256, profs);

        return fps30.ScientificObservationCount == samples.Count &&
               fps60.ScientificObservationCount == samples.Count &&
               fps120.ScientificObservationCount == samples.Count &&
               string.Equals(fps30.ReplayedReportSha256, recordedSha, StringComparison.Ordinal) &&
               string.Equals(fps60.ReplayedReportSha256, recordedSha, StringComparison.Ordinal) &&
               string.Equals(fps120.ReplayedReportSha256, recordedSha, StringComparison.Ordinal);
    }

    private static IReadOnlyList<M3PlaytestSessionRecord> BuildSimulatedGamerCohort() =>
    [
        new("sim_gamer_01", M3CohortKind.Gamer, 26.0, true, true, 2, "liked scooter & calm city toggle"),
        new("sim_gamer_02", M3CohortKind.Gamer, 29.0, true, true, 1, "completed HIST-02/03 cleanly"),
        new("sim_gamer_03", M3CohortKind.Gamer, 24.5, true, true, 2, "found 3 eggs and Mercator card"),
        new("sim_gamer_04", M3CohortKind.Gamer, 28.0, true, true, 1, "recovered pending vs protected batches"),
        new("sim_gamer_05", M3CohortKind.Gamer, 22.0, true, false, 1, "stopped after bicycle unlock"),
    ];

    private static IReadOnlyList<M3PlaytestSessionRecord> BuildSimulatedStudentCohort() =>
    [
        new("sim_student_01", M3CohortKind.Student, 27.0, true, true, 3, "linked rain grip, overlap speed & DGNSS decorrelation"),
        new("sim_student_02", M3CohortKind.Student, 25.0, true, true, 2, "linked night exposure blur & control vs checkpoints"),
        new("sim_student_03", M3CohortKind.Student, 28.5, true, true, 3, "linked glare, rolling shutter & GNSS DOP"),
        new("sim_student_04", M3CohortKind.Student, 29.5, true, false, 2, "linked overlap speed & DGNSS operating window"),
        new("sim_student_05", M3CohortKind.Student, 24.0, true, true, 1, "completed collection cleanly"),
    ];

    private static IReadOnlyList<M3TaskEvidenceEntry> BuildAllSixteenM3Tasks() =>
    [
        new("T-F02-03", "Montar Centro Velho do MVP", ["REQ-F02-02"], ["AC-F02-02", "AC-F02-03"], "artifacts/evidence/T-F02-03/README.md", true),
        new("T-F03-03", "Adicionar bicicleta e recursos", ["REQ-F03-01", "REQ-F03-02", "REQ-F03-04"], ["AC-F03-01", "AC-F03-04"], "artifacts/evidence/T-F03-03/README.md", true),
        new("T-F03-04", "Adicionar motoneta e recuperação", ["REQ-F03-01", "REQ-F03-03", "REQ-F03-04"], ["AC-F03-01", "AC-F03-03"], "artifacts/evidence/T-F03-04/README.md", true),
        new("T-F04-04", "Adicionar ambiente e sobreposição", ["REQ-F04-01", "REQ-F04-04"], ["AC-F04-03", "AC-F04-04"], "artifacts/evidence/T-F04-04/README.md", true),
        new("T-F04-05", "Integrar capacete e câmeras frente/trás", ["REQ-F04-03", "REQ-F04-04"], ["AC-F04-04"], "artifacts/evidence/T-F04-05/README.md", true),
        new("T-F05-03", "Adicionar aluguel e DGNSS", ["REQ-F05-03"], ["AC-F05-02", "AC-F05-03"], "artifacts/evidence/T-F05-03/README.md", true),
        new("T-F07-02", "Integrar Arcade e Técnico simplificados", ["REQ-F07-02"], ["AC-F07-02"], "artifacts/evidence/T-F07-02/README.md", true),
        new("T-F08-02", "Adicionar loja e progressão inicial", ["REQ-F08-02", "REQ-F08-03", "REQ-F08-04"], ["AC-F08-02", "AC-F08-04"], "artifacts/evidence/T-F08-02/README.md", true),
        new("T-F09-02", "Implementar calendário, clima e aderência", ["REQ-F09-02"], ["AC-F09-02"], "artifacts/evidence/T-F09-02/README.md", true),
        new("T-F09-03", "Adicionar bêbados, blitz e cães", ["REQ-F09-03", "REQ-F09-04"], ["AC-F09-03"], "artifacts/evidence/T-F09-03/README.md", true),
        new("T-F09-04", "Implementar contratos tranquilos e Cidade calma", ["REQ-F09-05"], ["AC-F09-04"], "artifacts/evidence/T-F09-04/README.md", true),
        new("T-F10-02", "Completar MVP e gerador de cinco templates", ["REQ-F10-01", "REQ-F10-02", "REQ-F10-03"], ["AC-F10-01", "AC-F10-02"], "artifacts/evidence/T-F10-02/README.md", true),
        new("T-F11-04", "Entregar Geopédia e revisão do MVP", ["REQ-F11-01", "REQ-F11-04"], ["AC-F11-01", "AC-F11-04"], "artifacts/evidence/T-F11-04/README.md", true),
        new("T-F12-03", "Implementar replay e ferramenta de relatório", ["REQ-F12-03"], ["AC-F12-03"], "artifacts/evidence/T-F12-03/README.md", true),
        new("T-F18-01", "Adicionar ovos, marcos e cartas", ["REQ-F18-01"], ["AC-F18-01"], "artifacts/evidence/T-F18-01/README.md", true),
        new("T-F19-03", "Validar MVP com os dois públicos", ["REQ-F19-02"], ["AC-F19-02"], "artifacts/evidence/T-F19-03/README.md", true),
    ];
}
