using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Contracts;
using Gsa.Missions.Gameplay;
using Gsa.Missions.Office;

namespace Gsa.Missions.Presentation;

public sealed record SurveyTargetDiagnostic(
    string TargetId, int Observations, int UsableObservations, bool Resolved,
    double ParallaxDegrees, string ExplanationKey);

public sealed record SurveyPhotoReview(
    CameraObservationSample Sample, bool Protected, IReadOnlyList<string> FailureKeys);

public sealed record SurveyNotebookView(
    IReadOnlyList<SurveyTargetDiagnostic> Targets,
    IReadOnlyList<SurveyPhotoReview> Photos,
    QualityReportView? LastProcessedReport,
    int UnprocessedPhotoCount);

/// <summary>Read-only diagnostics derived from measurements and the last processed report, never target truth.</summary>
public static class SurveyNotebookPresenter
{
    public static SurveyNotebookView Build(TrainingContractSession session, string locale)
    {
        var samples = session.Samples.ToArray();
        var inventory = FeatureInventoryTriangulator.BuildInventory(samples, FeatureConfirmationMode.Arcade);
        var targets = TrainingContractSession.TargetIds.Select(id =>
        {
            var observations = samples.Where(s => s.ObservedFeatures.Any(f => f.TargetId == id)).ToArray();
            var entry = inventory.Entries.FirstOrDefault(e => e.TargetId == id);
            var resolved = entry?.Status == TriangulationStatus.Resolved && entry.Confirmed;
            var key = resolved ? "notebook.geometry.resolved" : entry?.FailureReason switch
            {
                GeometryFailureReason.QualityRejected => "notebook.geometry.quality",
                GeometryFailureReason.SingleFrameWithoutSupportPlane => "notebook.geometry.single",
                GeometryFailureReason.LowParallaxAngle => "notebook.geometry.parallax",
                GeometryFailureReason.BehindCamera => "notebook.geometry.behind",
                GeometryFailureReason.LowPlaneGrazingAngle => "notebook.geometry.grazing",
                GeometryFailureReason.UnconfirmedClassOrFeature => "notebook.geometry.unconfirmed",
                _ => "notebook.geometry.unseen",
            };
            return new SurveyTargetDiagnostic(id, observations.Length, observations.Count(s => s.Quality.Passed),
                resolved, entry?.MaxParallaxDegrees ?? 0, key);
        }).ToArray();
        var protectedIds = session.Contract.ProtectedBatches.Select(b => b.BatchId).ToHashSet(StringComparer.Ordinal);
        var photos = samples.Select(sample =>
        {
            var failures = new List<string>();
            if (sample.Quality.MotionBlurExceeded) failures.Add("notebook.photo.blur");
            if (sample.Quality.ClippingExceeded) failures.Add("notebook.photo.clipping");
            if (sample.Quality.LensObstructionExceeded) failures.Add("notebook.photo.lens");
            if (sample.Quality.InsufficientVisibleFeatures) failures.Add("notebook.photo.empty");
            return new SurveyPhotoReview(sample, protectedIds.Contains($"live_{sample.SampleId}"), failures);
        }).ToArray();
        var report = session.Report is null ? null : BoardOfficeAndReportPresenter.BuildQualityReportView(
            session.Contract.Snapshot!.Definition, session.Report, null, ProcessingMode.Technical, locale);
        var lastProcess = session.Contract.Events.LastOrDefault(e => e.EventType == "ProcessingCompletedEnteredQc");
        var processedIds = lastProcess is null ? new HashSet<string>(StringComparer.Ordinal) : session.Contract.Events
            .Where(e => e.EventType == "BatchProtected" && e.Sequence < lastProcess.Sequence)
            .Select(e => e.BatchId!).ToHashSet(StringComparer.Ordinal);
        return new SurveyNotebookView(targets, photos, report,
            samples.Count(s => !processedIds.Contains($"live_{s.SampleId}")));
    }
}
