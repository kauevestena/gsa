using Gsa.Missions.Campaign;

namespace Gsa.Missions.Diagnostics;

public sealed record M2PlaytestSessionRecord(
    string ParticipantId,
    bool HasPriorGeomaticsKnowledge,
    double SessionDurationMinutes,
    bool CompletedContract,
    int InterventionsCount,
    bool IdentifiedRejectionCause,
    bool IdentifiedCorrectiveAction,
    bool ChoseAnotherContract,
    string DiscomfortNotes,
    string ControlNotes,
    string ConfusionNotes,
    bool IsHumanCohortSession = false)
{
    public bool CompletedWithinTwoInterventions => CompletedContract && InterventionsCount <= 2;
    public bool IdentifiedCauseAndCorrection => IdentifiedRejectionCause && IdentifiedCorrectiveAction;
}

public sealed record M2CorrectionItem(
    string Id,
    string Cause,
    string CorrectiveTask,
    string Component,
    bool VerifiedByRetest);

public sealed record M2TaskEvidenceEntry(
    string TaskId,
    string Title,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<string> AcceptanceScenarios,
    string EvidencePath,
    bool Accepted,
    bool EvidenceVerified = true);

public sealed record M2ProtocolThresholds(
    int RequiredParticipants,
    double MaxDurationMinutes,
    int MinCompletedWithinTwoInterventions,
    int MinIdentifiedCauseAndCorrection,
    int MinChoseAnotherContract);

public sealed record M2ObservedCohortCounts(
    int TotalParticipants,
    int EligibleParticipants,
    int QualifiedCompletedWithinTwoInterventions,
    int QualifiedIdentifiedCauseAndCorrection,
    int QualifiedChoseAnotherContract,
    double MaxObservedDurationMinutes,
    bool ContainsDisqualifiedParticipants);

public sealed record M2CohortEvaluationResult(
    bool CriteriaMet,
    string PlaytestGateStatus,
    M2ObservedCohortCounts ObservedCounts,
    IReadOnlyList<M2CorrectionItem> EmittedCorrections);

public sealed record M2GateReport(
    int SchemaVersion,
    string Milestone,
    string TechnicalLoopStatus,
    string PlaytestGateStatus,
    string OverallGateStatus,
    TutorialPlaythroughReport TutorialPlaythroughSummary,
    M2ProtocolThresholds ProtocolThresholds,
    M2ObservedCohortCounts ObservedCounts,
    IReadOnlyList<M2PlaytestSessionRecord> Sessions,
    IReadOnlyList<M2CorrectionItem> CorrectionsLog,
    IReadOnlyList<M2TaskEvidenceEntry> Tasks);

/// <summary>Enforces M2 playtest and loop-closure criteria without relaxing thresholds.</summary>
public static class M2PlaytestAndGateValidator
{
    public static M2ProtocolThresholds NormativeThresholds { get; } = new(
        RequiredParticipants: 5,
        MaxDurationMinutes: 30.0,
        MinCompletedWithinTwoInterventions: 4,
        MinIdentifiedCauseAndCorrection: 4,
        MinChoseAnotherContract: 3);

    private static readonly IReadOnlyList<M2CorrectionItem> ResolvedM2Corrections =
    [
        new M2CorrectionItem(
            "CORR-M2-01",
            "Cancelling an active office processing job left ContractStateMachine stranded in Processando instead of returning to Coletando/Retrabalho.",
            "T-F07-01",
            "Gsa.Missions.Contracts.ContractStateMachine.CancelProcessing",
            true),
        new M2CorrectionItem(
            "CORR-M2-02",
            "System.Text.Json deserialized readonly Vector3d, Quaterniond, and Bounds2DEnu structs as default zeros during save/batch restoration.",
            "T-F12-02",
            "Gsa.Core.Geometry & Gsa.Missions.Quality [JsonConstructor]",
            true),
        new M2CorrectionItem(
            "CORR-M2-03",
            "Provisional HUD coverage needed explicit non-CQ warning indicator and report cards needed direct privacy/recollection actions before second playtest attempt.",
            "T-F11-03",
            "Gsa.Missions.Presentation.BoardOfficeAndReportPresenter",
            true),
    ];

    public static M2CohortEvaluationResult EvaluateCohort(
        IReadOnlyList<M2PlaytestSessionRecord> sessions,
        IReadOnlyList<M2CorrectionItem>? existingCorrections = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var corrections = new List<M2CorrectionItem>(existingCorrections ?? []);
        var t = NormativeThresholds;

        if (sessions.Count < t.RequiredParticipants)
        {
            corrections.Add(new M2CorrectionItem(
                $"CORR-COHORT-{corrections.Count + 1:00}",
                $"Insufficient novice participants ({sessions.Count}/{t.RequiredParticipants} required by M2 protocol).",
                "T-F19-02",
                "PlaytestCohortRecruitment",
                false));
        }

        var eligibleSessions = new List<M2PlaytestSessionRecord>(sessions.Count);
        var containsDisqualified = false;
        var maxDuration = 0.0;

        foreach (var session in sessions)
        {
            if (!double.IsFinite(session.SessionDurationMinutes) || session.SessionDurationMinutes <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(sessions), $"Session '{session.ParticipantId}' duration must be finite and positive.");

            if (session.SessionDurationMinutes > maxDuration)
                maxDuration = session.SessionDurationMinutes;

            if (session.HasPriorGeomaticsKnowledge)
            {
                containsDisqualified = true;
                corrections.Add(new M2CorrectionItem(
                    $"CORR-QUAL-{corrections.Count + 1:00}",
                    $"Participant '{session.ParticipantId}' has prior geomatics knowledge and is ineligible for the M2 novice gate.",
                    "T-F19-02",
                    "PlaytestCohortScreening",
                    false));
                continue;
            }

            if (session.SessionDurationMinutes > t.MaxDurationMinutes)
            {
                containsDisqualified = true;
                corrections.Add(new M2CorrectionItem(
                    $"CORR-TIME-{corrections.Count + 1:00}",
                    $"Participant '{session.ParticipantId}' exceeded the {t.MaxDurationMinutes:0} min M2 session limit ({session.SessionDurationMinutes:0.0} min).",
                    "T-F19-02",
                    "TutorialPacingAndOnboarding",
                    false));
                continue;
            }

            eligibleSessions.Add(session);
        }

        var completedWithinTwo = eligibleSessions.Count(s => s.CompletedWithinTwoInterventions);
        var identifiedCauseAndAction = eligibleSessions.Count(s => s.IdentifiedCauseAndCorrection);
        var choseAnother = eligibleSessions.Count(s => s.ChoseAnotherContract);

        if (eligibleSessions.Count < t.RequiredParticipants)
        {
            corrections.Add(new M2CorrectionItem(
                $"CORR-ELIGIBLE-{corrections.Count + 1:00}",
                $"Only {eligibleSessions.Count}/{t.RequiredParticipants} eligible novice sessions without prior geomatics or >30 min timeout.",
                "T-F19-02",
                "PlaytestCohortScreening",
                false));
        }

        if (completedWithinTwo < t.MinCompletedWithinTwoInterventions)
        {
            corrections.Add(new M2CorrectionItem(
                $"CORR-COMPLETION-{corrections.Count + 1:00}",
                $"Completion within <=2 interventions was {completedWithinTwo}/{t.RequiredParticipants} (requires >= {t.MinCompletedWithinTwoInterventions}).",
                "T-F10-01",
                "TutorialGuidanceAndControls",
                false));
        }

        if (identifiedCauseAndAction < t.MinIdentifiedCauseAndCorrection)
        {
            corrections.Add(new M2CorrectionItem(
                $"CORR-CAUSE-{corrections.Count + 1:00}",
                $"Identification of rejection cause + corrective action was {identifiedCauseAndAction}/{t.RequiredParticipants} (requires >= {t.MinIdentifiedCauseAndCorrection}).",
                "T-F11-03",
                "QualityReportPresentation",
                false));
        }

        if (choseAnother < t.MinChoseAnotherContract)
        {
            corrections.Add(new M2CorrectionItem(
                $"CORR-ENGAGEMENT-{corrections.Count + 1:00}",
                $"Repeat contract choice was {choseAnother}/{t.RequiredParticipants} (requires >= {t.MinChoseAnotherContract}).",
                "T-F19-02",
                "ContractLoopPacingAndReward",
                false));
        }

        var counts = new M2ObservedCohortCounts(
            sessions.Count,
            eligibleSessions.Count,
            completedWithinTwo,
            identifiedCauseAndAction,
            choseAnother,
            maxDuration,
            containsDisqualified);

        var criteriaMet = !containsDisqualified
            && eligibleSessions.Count >= t.RequiredParticipants
            && completedWithinTwo >= t.MinCompletedWithinTwoInterventions
            && identifiedCauseAndAction >= t.MinIdentifiedCauseAndCorrection
            && choseAnother >= t.MinChoseAnotherContract;

        var allHuman = sessions.Count > 0 && sessions.All(s => s.IsHumanCohortSession);

        var playtestStatus = !criteriaMet
            ? "failed_rework_required"
            : allHuman
                ? "passed"
                : "pending_human_playtest_cohort";

        return new M2CohortEvaluationResult(
            criteriaMet,
            playtestStatus,
            counts,
            Array.AsReadOnly(corrections.ToArray()));
    }

    public static M2GateReport BuildM2GateReport(
        TutorialPlaythroughReport tutorialReport,
        IReadOnlyList<M2PlaytestSessionRecord>? humanSessions = null)
    {
        ArgumentNullException.ThrowIfNull(tutorialReport);

        var technicalPassed = tutorialReport.FinalReportPassed
            && tutorialReport.FinalStars == 5
            && tutorialReport.IdempotentReplayConfirmed
            && string.Equals(tutorialReport.RawBatchSha256BeforeProcessing, tutorialReport.RawBatchSha256AfterProcessing, StringComparison.Ordinal)
            && !tutorialReport.RequiresFutureSensors
            && tutorialReport.FinalPrivacyCompliant
            && tutorialReport.SaveRoundTripVerified
            && tutorialReport.CatalogValidation.Valid
            && tutorialReport.Phases.Count == 4
            && tutorialReport.Phases.All(p => p.Passed);

        var technicalStatus = technicalPassed ? "passed" : "failed";

        var sessions = humanSessions ?? BuildDefaultNoviceSimulationCohort();
        var evaluation = EvaluateCohort(sessions, ResolvedM2Corrections);

        var overallStatus = !technicalPassed
            ? "failed_rework_required"
            : evaluation.PlaytestGateStatus;

        return new M2GateReport(
            1,
            "M2",
            technicalStatus,
            evaluation.PlaytestGateStatus,
            overallStatus,
            tutorialReport,
            NormativeThresholds,
            evaluation.ObservedCounts,
            Array.AsReadOnly(sessions.ToArray()),
            evaluation.EmittedCorrections,
            BuildM2TaskEvidenceRegistry());
    }

    private static IReadOnlyList<M2PlaytestSessionRecord> BuildDefaultNoviceSimulationCohort() =>
    [
        new("sim_novice_01", false, 18.5, true, 0, true, true, true, "none", "keyboard+mouse smooth", "none", IsHumanCohortSession: false),
        new("sim_novice_02", false, 22.0, true, 1, true, true, true, "none", "asked for unload icon", "privacy notice clear after card hint", IsHumanCohortSession: false),
        new("sim_novice_03", false, 24.5, true, 1, true, true, true, "none", "gamepad tested", "checked provisional banner vs final CQ", IsHumanCohortSession: false),
        new("sim_novice_04", false, 27.0, true, 2, true, true, true, "minor eye fatigue reading technical unit table", "switched back to Arcade report", "understood blur recollection target", IsHumanCohortSession: false),
        new("sim_novice_05", false, 21.0, true, 1, true, true, false, "none", "preferred board filter", "stopped after HIST-01 payment settled", IsHumanCohortSession: false),
    ];

    private static IReadOnlyList<M2TaskEvidenceEntry> BuildM2TaskEvidenceRegistry() =>
    [
        new("T-F04-03", "Implementar scheduler e inventário", ["REQ-F04-02", "REQ-F04-03"], ["AC-F04-02", "AC-F04-04"], "artifacts/evidence/T-F04-03/README.md", true),
        new("T-F05-02", "Integrar céu e estado de solução", ["REQ-F05-01", "REQ-F05-02"], ["AC-F05-01", "AC-F05-04"], "artifacts/evidence/T-F05-02/README.md", true),
        new("T-F06-02", "Implementar máquina de contratos", ["REQ-F06-02"], ["AC-F06-03", "AC-F06-04"], "artifacts/evidence/T-F06-02/README.md", true),
        new("T-F06-03", "Implementar CQ de fotos e inventário", ["REQ-F06-03", "REQ-F06-05"], ["AC-F06-01", "AC-F06-05"], "artifacts/evidence/T-F06-03/README.md", true),
        new("T-F06-04", "Implementar estrelas e valor de entrega", ["REQ-F06-04", "REQ-F06-05"], ["AC-F06-01", "AC-F06-03"], "artifacts/evidence/T-F06-04/README.md", true),
        new("T-F06-05", "Integrar retrabalho e liquidação idempotente", ["REQ-F06-02", "REQ-F06-04"], ["AC-F06-02", "AC-F06-04"], "artifacts/evidence/T-F06-05/README.md", true),
        new("T-F07-01", "Criar processamento básico por receita", ["REQ-F07-01"], ["AC-F07-01"], "artifacts/evidence/T-F07-01/README.md", true),
        new("T-F07-04", "Implementar máscaras e conformidade", ["REQ-F07-03"], ["AC-F07-03"], "artifacts/evidence/T-F07-04/README.md", true),
        new("T-F08-01", "Implementar ledger e pagamentos", ["REQ-F08-01"], ["AC-F08-01"], "artifacts/evidence/T-F08-01/README.md", true),
        new("T-F10-01", "Criar tutorial e catálogo inicial de história", ["REQ-F10-01", "REQ-F10-04"], ["AC-F10-01"], "artifacts/evidence/T-F10-01/README.md", Accepted: false, EvidenceVerified: true),
        new("T-F11-02", "Implementar HUD, mapa e ajustes", ["REQ-F11-02", "REQ-F11-05"], ["AC-F11-02"], "artifacts/evidence/T-F11-02/README.md", true),
        new("T-F11-03", "Criar quadro, escritório e relatório", ["REQ-F11-02", "REQ-F11-03"], ["AC-F11-02", "AC-F11-03"], "artifacts/evidence/T-F11-03/README.md", true),
        new("T-F12-01", "Implementar lotes e descarregamento atômico", ["REQ-F12-01"], ["AC-F12-01"], "artifacts/evidence/T-F12-01/README.md", true),
        new("T-F12-02", "Implementar save e recuperação", ["REQ-F12-02"], ["AC-F12-01", "AC-F12-02"], "artifacts/evidence/T-F12-02/README.md", true),
        new("T-F19-02", "Validar primeira experiência completa", ["REQ-F19-02"], ["AC-F19-02"], "artifacts/evidence/T-F19-02/README.md", Accepted: false, EvidenceVerified: true),
    ];
}
