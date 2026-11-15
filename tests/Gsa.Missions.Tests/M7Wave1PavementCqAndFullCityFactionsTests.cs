using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.Traffic;
using Gsa.Missions.Economy;
using Gsa.Missions.Quality;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class M7Wave1PavementCqAndFullCityFactionsTests
{
    private static (List<GeoreferencedLidarSample> Lidar, List<PavementCameraFootprintObservation> Cam, List<PavementSectionTruthReference> Truth)
        BuildSyntheticPavementCorridor(int stationCount, int? missingStationIndex = null, int? obstructedStationIndex = null)
    {
        const double originEast = 500000.0;
        const double centerNorth = 7400050.0;
        var lidar = new List<GeoreferencedLidarSample>();
        var cam = new List<PavementCameraFootprintObservation>();
        var truth = new List<PavementSectionTruthReference>();
        var sampleId = 1L;

        for (var i = 0; i < stationCount; i++)
        {
            var elev = 12.0 + 0.01 * i;
            var crossSlope = 0.02;
            var rut = 5.0;
            var iri = 1.20;
            var sectionId = $"corridor_van_strip_0_st_{i:000}";

            truth.Add(new PavementSectionTruthReference(
                SectionId: sectionId,
                ChainageMetres: i + 0.5,
                CenterPositionEnu: new Vector3d(originEast + i + 0.5, centerNorth, elev),
                ReferenceElevationMetres: elev,
                ReferenceCrossSlopeFraction: crossSlope,
                ReferenceRutDepthMillimetres: rut,
                ReferenceIriMPerKm: iri));

            if (i == missingStationIndex)
                continue;

            for (var k = 0; k < 16; k++)
            {
                var east = originEast + i + 0.05 + k * 0.055;
                var transverseOffset = (k % 2 == 0) ? -0.0625 : 0.0625;
                var sampleZ = elev + (transverseOffset > 0 ? 0.0025 : 0.0);
                lidar.Add(new GeoreferencedLidarSample(
                    SampleId: sampleId++,
                    PhysicalTimestampSeconds: i * 0.25 + k * 0.01,
                    MapPositionEnu: new Vector3d(east, centerNorth + transverseOffset, sampleZ),
                    Intensity: 0.9f,
                    ReturnNumber: 1,
                    IsSpuriousRain: false));
            }

            cam.Add(new PavementCameraFootprintObservation(
                SampleId: $"cam_st_{i:000}",
                CameraSlotId: "slot_pavement_cam_a",
                TimestampSeconds: i * 0.25,
                ChainageMetres: i + 0.5,
                TransverseStripIndex: 0,
                TransverseOffsetMetres: 0.0,
                EstimatedElevationMetres: elev,
                ObservedRutDepthMillimetres: rut,
                LensObstructionFraction: i == obstructedStationIndex ? 0.65 : 0.01,
                IsOccluded: false));
        }

        return (lidar, cam, truth);
    }

    [Fact]
    public void T_F04_07_Cq_ExtractedPavementStationsPassStrictThresholdsAndExplicitGapsAreRejectedNotFabricated()
    {
        // Complete 20-station corridor passes pavement CQ thresholds cleanly against truth checkpoints.
        var (completeLidar, completeCam, truth) = BuildSyntheticPavementCorridor(20);
        var extractedComplete = PavementProfileExtractor.ExtractLongitudinalProfile(
            "corridor_van", 0.0, 20.0, stripIndex: 0, completeLidar, completeCam);

        Assert.Equal(0, extractedComplete.ExplicitGapCount);
        var completeObs = extractedComplete.Stations
            .Where(s => !s.IsExplicitGap)
            .Select(s => new PavementSectionObservation(
                s.SectionId,
                s.ChainageMetres,
                s.TransverseOffsetMetres,
                s.EstimatedElevationMetres!.Value,
                s.EstimatedCrossSlopeFraction!.Value,
                s.EstimatedRutDepthMillimetres!.Value,
                s.EstimatedIriMPerKm!.Value,
                s.SupportingSampleCount))
            .ToArray();

        var evaluator = new PavementProfileEvaluator();
        var spec = new PavementProfileQualitySpec("CONTRACT-PAV-M7");
        var passReport = evaluator.Evaluate(spec, truth, completeObs);

        Assert.True(passReport.Passed);
        Assert.Equal(20, passReport.ExpectedSectionCount);
        Assert.Equal(20, passReport.CoveredSectionCount);
        Assert.Equal(1.0, passReport.SectionCoverageFraction, 6);
        Assert.True(passReport.ElevationRmseMetres!.Value <= 0.020);
        Assert.True(passReport.CrossSlopeRmseFraction!.Value <= 0.005);
        Assert.True(passReport.RutDepthRmseMillimetres!.Value <= 3.0);
        Assert.True(passReport.IriRmseMPerKm!.Value <= 0.50);

        // Corridor with missing & obstructed stations preserves explicit gaps (90% < 95% coverage) -> CQ rejects and emits recollection areas.
        var (gapLidar, gapCam, gapTruth) = BuildSyntheticPavementCorridor(20, missingStationIndex: 6, obstructedStationIndex: 13);
        var extractedWithGaps = PavementProfileExtractor.ExtractLongitudinalProfile(
            "corridor_van", 0.0, 20.0, stripIndex: 0, gapLidar, gapCam);

        Assert.Equal(2, extractedWithGaps.ExplicitGapCount);
        var nonGapObs = extractedWithGaps.Stations
            .Where(s => !s.IsExplicitGap)
            .Select(s => new PavementSectionObservation(
                s.SectionId,
                s.ChainageMetres,
                s.TransverseOffsetMetres,
                s.EstimatedElevationMetres!.Value,
                s.EstimatedCrossSlopeFraction!.Value,
                s.EstimatedRutDepthMillimetres!.Value,
                s.EstimatedIriMPerKm!.Value,
                s.SupportingSampleCount))
            .ToArray();

        var failReport = evaluator.Evaluate(spec, gapTruth, nonGapObs);
        Assert.False(failReport.Passed);
        Assert.Equal(18, failReport.CoveredSectionCount);
        Assert.Equal(0.90, failReport.SectionCoverageFraction, 6);
        Assert.Contains("cq.failure.pavement_coverage", failReport.FailureReasonKeys);
        Assert.Contains("cq.failure.pavement_section_sparse_or_missing", failReport.FailureReasonKeys);
        Assert.Equal(2, failReport.RecollectionAreas.Count);
    }

    [Fact]
    public void T_F16_04_ProtectedSafeZonesFourRegionsFiveFactionsNonViolentPlaythroughAndCalmCityParity()
    {
        // Protected safe zones cover all 4 sanctuary types across all 4 regions.
        var safeKinds = FullCityFactionPressureCoordinator.ProtectedSafeZones
            .Select(z => z.Kind)
            .Distinct()
            .ToArray();
        Assert.Equal(4, safeKinds.Length);

        var safeRegions = FullCityFactionPressureCoordinator.ProtectedSafeZones
            .Select(z => z.RegionId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["old_center", "port", "serra", "waterfront"], safeRegions);

        // Outside safe zone active capture raises attention; inside safe zone suppresses escalation, clears hostiles, and never allows weapons.
        var calmPolicy = new CalmPolicyCoordinator(calmCityEnabled: false);
        var tracker = new TerritoryAttentionTracker();
        var officeZone = FullCityFactionPressureCoordinator.ProtectedSafeZones.First(z => z.Kind == ProtectedSafeZoneKind.Office);

        Assert.True(FullCityFactionPressureCoordinator.IsInsideProtectedSafeZone(officeZone.RegionId, officeZone.CenterEnu, out var matched));
        Assert.Equal(officeZone.SafeZoneId, matched!.SafeZoneId);

        var outsidePos = officeZone.CenterEnu + new Vector3d(200.0, 0.0, 0.0);
        var outsideStep = FullCityFactionPressureCoordinator.EvaluateProtectedSafeZoneStep(
            officeZone.SafeZoneId, outsidePos, tracker, calmPolicy, linkedTerritoryZoneId: "zone_old_center_marco", dtSeconds: 6.0);
        Assert.False(outsideStep.InsideSafeZone);
        Assert.True(outsideStep.AttentionAfter > outsideStep.AttentionBefore);
        Assert.False(outsideStep.PlayerCanUseWeapons);

        calmPolicy.TryQueueHostileEvent("pursuit_test_event");
        Assert.Equal(1, calmPolicy.PendingHostileEventCount);

        var insideStep = FullCityFactionPressureCoordinator.EvaluateProtectedSafeZoneStep(
            officeZone.SafeZoneId, officeZone.CenterEnu, tracker, calmPolicy, linkedTerritoryZoneId: "zone_old_center_marco", dtSeconds: 6.0);
        Assert.True(insideStep.InsideSafeZone);
        Assert.True(insideStep.AttentionAfter <= insideStep.AttentionBefore);
        Assert.True(insideStep.EscalationSuppressed);
        Assert.True(insideStep.HostileQueueCleared);
        Assert.False(insideStep.PlayerCanUseWeapons);

        // Full-city playthrough parity between Standard Mode and Calm City across all 4 regions and all 5 factions.
        var comparison = FullCityFactionPressureCoordinator.CompareStandardAndCalmCityPlaythroughs();
        Assert.True(comparison.BothModesCompletableWithoutCombat);
        Assert.True(comparison.CqStrictnessIdenticalBetweenModes);
        Assert.True(comparison.CalmModeSuppressesHostileEscalation);

        Assert.Equal(["old_center", "port", "serra", "waterfront"], comparison.CoveredRegionIds);
        Assert.Equal(5, comparison.CoveredFactionIds.Count);

        foreach (var modeReport in new[] { comparison.StandardMode, comparison.CalmCityMode })
        {
            Assert.True(modeReport.Succeeded);
            Assert.Equal(4, modeReport.CoveredRegionCount);
            Assert.Equal(5, modeReport.CoveredFactionCount);
            Assert.True(modeReport.ZeroCombatDeadlocks);
            Assert.True(modeReport.ZeroWeaponsAllowed);
            Assert.True(modeReport.ZeroInjuries);
            Assert.Equal(FullCityFactionPressureCoordinator.ProtectedSafeZones.Count, modeReport.ProtectedSafeZoneVerifiedCount);
            Assert.True(modeReport.ZoneSteps.All(s => s.GnssSpoofingHandledSafely && s.MovedBenchmarkVerifiedWithoutHistoricalMutation));
        }

        // Standard mode exercises both associacao_bairro authorization and layered countermeasures cleanly.
        Assert.Contains(comparison.StandardMode.ZoneSteps, s => s.CountermeasureCommunityAuth);
        Assert.Contains(comparison.StandardMode.ZoneSteps, s => s.CountermeasureEscort && s.CountermeasureDisguise && s.CountermeasureOffPeak);
    }
}

