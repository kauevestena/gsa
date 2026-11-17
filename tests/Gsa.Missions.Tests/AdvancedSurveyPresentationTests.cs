using System.Globalization;
using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.Traffic;
using Gsa.Missions.Contracts;
using Gsa.Missions.Presentation;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class AdvancedSurveyPresentationTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Gsa.Domain.slnf"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    [Fact]
    public void AllAdvancedArticlesHaveBilingualExamplesUnitsLimitsAndGlossaryTerms()
    {
        var root = RepoRoot();
        var enPath = Path.Combine(root, "game/localization/en_US.po");
        var ptPath = Path.Combine(root, "game/localization/pt_BR.po");
        var audit = GeopediaCatalogCoordinator.AuditBilingualCatalog(enPath, ptPath, Path.Combine(root, "content/localization/glossary.csv"));
        Assert.True(audit.Passed);
        Assert.Equal(30, GeopediaCatalogCoordinator.AllEntries.Count);
        Assert.Equal(30, GeopediaCatalogCoordinator.AllEntries.Select(e => e.Id).Distinct().Count());
        var geo = new GeopediaCatalogCoordinator();
        foreach (var entry in GeopediaCatalogCoordinator.AdvancedEntries)
        {
            Assert.True(geo.UnlockByConceptId(entry.ConceptId));
            foreach (var (path, locale) in new[] { (enPath, "en-US"), (ptPath, "pt-BR") })
            {
                var article = geo.RenderEntry(entry.Id, locale, GeopediaCatalogCoordinator.ParseSimplePoTranslations(path));
                Assert.True(article.Unlocked);
                Assert.NotEqual(entry.TitleKey, article.Title);
                Assert.NotEqual(entry.ExampleKey, article.GameExample);
                Assert.NotEqual(entry.UnitAndEffectKey, article.UnitAndEffect);
                Assert.NotEqual(entry.ModelLimitKey, article.ModelLimit);
                Assert.All(new[] { article.GameExample, article.UnitAndEffect, article.ModelLimit }, text => Assert.True(text.Length > 40));
            }
        }
        var restoredIds = JsonSerializer.Deserialize<string[]>(JsonSerializer.Serialize(geo.ExportUnlockedIds()))!;
        var restored = new GeopediaCatalogCoordinator(restoredIds);
        Assert.All(GeopediaCatalogCoordinator.AdvancedEntries, e => Assert.True(restored.IsUnlocked(e.Id)));
        Assert.Equal(0, restored.UnlockForMilestone("M4"));
    }

    [Fact]
    public void EquipmentUnlocksAdvancedConceptsIncrementallyWithoutAwardingCollectibles()
    {
        var geo = new GeopediaCatalogCoordinator();
        Assert.Equal(0, geo.UnlockAdvancedForPlatformLevel(2));
        Assert.Equal(9, geo.UnlockAdvancedForPlatformLevel(3));
        Assert.False(geo.IsUnlocked("geopedia_lidar_density"));
        Assert.Equal(4, geo.UnlockAdvancedForPlatformLevel(4));
        Assert.False(geo.IsUnlocked("geopedia_lidar_returns"));
        Assert.Equal(1, geo.UnlockAdvancedForPlatformLevel(5));
        Assert.Equal(0, geo.UnlockAdvancedForPlatformLevel(6));
        Assert.Equal(0, geo.UnlockAdvancedForPlatformLevel(3));
        Assert.Equal(14, geo.ExportUnlockedIds().Count);
        Assert.All(GeopediaCatalogCoordinator.MvpEntries, e => Assert.False(geo.IsUnlocked(e.Id)));
        Assert.Throws<ArgumentOutOfRangeException>(() => geo.UnlockAdvancedForPlatformLevel(-1));
    }

    [Theory]
    [InlineData(4, 1, "1000", "1000")]
    [InlineData(5, 2, "8000", "4000")]
    [InlineData(6, 4, "16000", "4000")]
    public void PreviewRatesMatchActualSlicedPrimaryRaySchedule(int grade, int channelStep, string nominal, string scheduled)
    {
        var profile = LidarProfile.AllProfiles.Single(p => p.Grade == grade);
        var view = AdvancedSurveyPresenter.BuildLidarPreview(profile, true, channelStep, 17, 18000, "en-US");
        Assert.Equal(nominal, view.NominalPrimaryRaysPerSecond);
        Assert.Equal(scheduled, view.ScheduledPrimaryRaysPerSecond);
        Assert.Equal("17", view.VisiblePoints);
        var simulator = new LidarBenchSimulator(profile);
        var rotation = Quaterniond.Identity;
        var trajectory = new EstimatedBodyTrajectory([new(0, Vector3d.Zero, rotation), new(1, Vector3d.Zero, rotation)]);
        var rays = 0;
        LidarRayHit? Cast(Vector3d origin, Vector3d direction, double range) { rays++; return null; }
        var returns = new List<LidarMeasuredReturn>();
        var slices = (int)(60 / profile.ScanRateHz);
        for (var i = 0; i < slices; i++)
            Assert.True(simulator.TrySimulateScanSlice(0, RigidTransform.Identity, trajectory, LidarMountEstimate.Zero,
                false, Cast, i, slices, returns, channelStep));
        Assert.Equal(double.Parse(scheduled, CultureInfo.InvariantCulture), rays * profile.ScanRateHz);
    }

    [Fact]
    public void PreviewShowsLocalRangeNoiseWithoutPromotingItToProductAccuracy()
    {
        var view = AdvancedSurveyPresenter.BuildLidarPreview(LidarProfile.L6, false, 4, 18000, 18000, "pt_BR");
        Assert.Equal("0,005", view.RangeSigmaMetres);
        Assert.Equal("survey.technical.lidar_off", view.StatusKey);
        Assert.Equal("18000", view.VisiblePoints);
        Assert.Throws<ArgumentOutOfRangeException>(() => AdvancedSurveyPresenter.BuildLidarPreview(LidarProfile.L6, true, 0, 0, 18000, "en-US"));
        Assert.Throws<ArgumentOutOfRangeException>(() => AdvancedSurveyPresenter.BuildLidarPreview(LidarProfile.L6, true, 4, 18001, 18000, "en-US"));
    }

    [Theory]
    [InlineData("pt-BR", "0,000125", "0,9")]
    [InlineData("en-US", "0.000125", "0.9")]
    public void TechnicalRequirementsKeepExactLocalThresholdsDirectionsAndDensityContext(string locale, string fineLimit, string ratio)
    {
        var contract = ContractDefinition.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/facades.json"))) with
        {
            Requirements = [
                new ThresholdRequirementSpec("absolute_rmse", "m", "max", .000125, .0000625),
                new ThresholdRequirementSpec("density_coverage", "ratio", "min", .9, .99, "pavement", 1, 20),
                new PredicateRequirementSpec("format_valid", true),
                new PredicateRequirementSpec("restricted_area", false),
            ],
            PartialPolicy = new("deliverables", [new("south_strip", 1, "south_area", [],
                [new ThresholdRequirementSpec("density_coverage", "ratio", "min", .85, .95, "south_pavement", .5, 12)])]),
        };
        var before = JsonSerializer.Serialize(contract);
        var rows = AdvancedSurveyPresenter.BuildRequirements(contract, locale);
        Assert.Equal(fineLimit, rows[0].Limit);
        Assert.Equal("≤", rows[0].Comparison);
        Assert.Equal(.000125, double.Parse(rows[0].Limit, CultureInfo.GetCultureInfo(locale)));
        Assert.Equal(ratio, rows[1].Limit);
        Assert.Equal("≥", rows[1].Comparison);
        Assert.Equal("ratio", rows[1].Unit);
        Assert.Equal("pavement", rows[1].SurfaceId);
        Assert.Equal("1", rows[1].CellSizeMetres);
        Assert.Equal("20", rows[1].TargetDensityPointsPerM2);
        Assert.True(rows[2].Expected);
        Assert.False(rows[3].Expected);
        Assert.Equal(5, rows.Count);
        Assert.Equal("south_strip", rows[4].DeliverableId);
        Assert.Equal("south_area", rows[4].AreaId);
        Assert.Equal("south_pavement", rows[4].SurfaceId);
        Assert.Equal("12", rows[4].TargetDensityPointsPerM2);
        Assert.Equal(before, JsonSerializer.Serialize(contract));
        Assert.Empty(AdvancedSurveyPresenter.BuildRequirements(null, locale));
    }

    [Fact]
    public void AttentionSnapshotMatchesTrackerAndKeepsProtectionSeparateFromRecordedScore()
    {
        var zone = FactionCatalog.DefaultTerritoryZones[0];
        var tracker = new TerritoryAttentionTracker([zone]);
        tracker.Step(zone.ZoneId, true, 1, false, 41);
        var view = Assert.Single(AdvancedSurveyPresenter.BuildAttentionZones([zone], tracker, protectionActive: true));
        Assert.Equal(41, view.AttentionScore);
        Assert.Equal(2, view.ThreatLevel);
        Assert.True(view.ProtectionActive);
        Assert.Equal(zone.Bounds, view.Bounds);
        Assert.Equal(FactionCatalog.GetFaction(zone.ControllingFaction).NameKey, view.FactionNameKey);
        tracker.Step(null, false, 0, false, 10);
        Assert.Equal(41, view.AttentionScore); // Immutable display snapshot, not a live mutable view.
        Assert.Equal(21, tracker.GetAttention(zone.ZoneId));
    }
}
