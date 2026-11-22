using Gsa.Core.Geometry;
using Gsa.Core.World;
using Gsa.Missions.Quality;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class SerraBoundaryQualityTests
{
    [Fact]
    public void OfficeRequiresTwoIndependentAgreeingReadingsAndRecomputesDisplacement()
    {
        var sites = SerraBoundarySurvey.Sites;
        var shots = sites.SelectMany((site, i) => Enumerable.Range(0, 2).Select(station => SerraBoundarySurvey.Observe(
            site, station, SerraBoundarySurvey.DisplacedPosition(site, i), i * 2 + station + 1, 100))).ToArray();
        SerraBoundaryReview Evaluate(params BoundarySurveyShot[] revision) =>
            SerraBoundaryQuality.Evaluate(SerraBoundarySurvey.MissionId, ["protected"], revision);

        var solved = Evaluate(shots);
        Assert.True(solved.Delivery.Passed);
        Assert.All(solved.Monuments, row => Assert.True(row.Displaced));
        Assert.All(solved.Monuments, row => Assert.InRange(row.RepeatErrorMetres!.Value, 0, .02));

        var duplicate = Evaluate([.. shots.Skip(1), shots[1] with { PhysicsTick = 100 }]);
        Assert.False(duplicate.Delivery.Passed);
        Assert.False(duplicate.Monuments[0].HasStationA);

        var offPosition = SerraBoundarySurvey.DisplacedPosition(sites[0], 0) + Vector3d.UnitX;
        var inconsistent = Evaluate([.. shots, SerraBoundarySurvey.Observe(sites[0], 1, offPosition, 100, 101)]);
        Assert.False(inconsistent.Delivery.Passed);
        Assert.False(inconsistent.Monuments[0].Resolved);

        var nominal = Evaluate([.. shots.Skip(2), SerraBoundarySurvey.Observe(sites[0], 0, sites[0].PublishedPositionEnu, 101, 102),
            SerraBoundarySurvey.Observe(sites[0], 1, sites[0].PublishedPositionEnu, 102, 102)]);
        Assert.False(nominal.Delivery.Passed);
        Assert.True(nominal.Monuments[0].Resolved);
        Assert.False(nominal.Monuments[0].Displaced);
    }
}
