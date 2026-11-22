using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.World;
using Gsa.Missions.Quality;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class PortWorksQualityTests
{
    [Fact]
    public void OfficeRequiresCoverageOnEverySideAndEvidenceOfAFieldUpload()
    {
        var allPoints = new List<GeoreferencedLidarSample>();
        var omitted = new HashSet<long>();
        foreach (var side in PortWorksSurvey.Sides)
        {
            var direction = (side.End - side.Start).Normalized();
            var across = new Vector3d(-direction.Y, direction.X, 0);
            for (var along = 0; along < 250; along++)
                for (var lane = 0; lane < 10; lane++)
                {
                    var p = side.Start + direction * ((along + .5) * .4) + across * (-1.8 + lane * .4);
                    var panel = side.Checkpoint;
                    var z = Math.Abs(p.X - panel.X) <= 1 && Math.Abs(p.Y - panel.Y) <= 1 ? panel.Z : PortWorksSurvey.RoadUp;
                    var id = allPoints.Count + 1L;
                    allPoints.Add(new(id, id * .01, new(p.X, p.Y, z), .8f, 1, false));
                    if (side.Id == "south" && along < 60) omitted.Add(id);
                }
        }

        var incomplete = new PointCloudOctreeAndChunkStore();
        incomplete.IngestRevision(allPoints.Where(point => !omitted.Contains(point.SampleId)));
        var gap = PortWorksQuality.Evaluate(PortWorksSurvey.MissionId, ["protected"], incomplete, fieldUploads: 1);
        Assert.True(gap.Cloud.CoverageFraction >= .90);
        Assert.False(gap.Delivery.Passed);
        Assert.Equal("south", Assert.Single(gap.Sides, side => side.Fraction < .90).Side);

        var complete = new PointCloudOctreeAndChunkStore();
        complete.IngestRevision(allPoints);
        var office = PortWorksQuality.Evaluate(PortWorksSurvey.MissionId, ["protected"], complete, fieldUploads: 0);
        Assert.True(office.Cloud.Passed);
        Assert.False(office.Delivery.Passed);

        var field = PortWorksQuality.Evaluate(PortWorksSurvey.MissionId, ["protected"], complete, fieldUploads: 1);
        Assert.True(field.Delivery.Passed);
    }
}
