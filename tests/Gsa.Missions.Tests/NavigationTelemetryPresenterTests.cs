using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Sensors.Gnss;
using Gsa.Missions.Presentation;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class NavigationTelemetryPresenterTests
{
    [Theory]
    [InlineData("pt_BR", "0,500", "5,100")]
    [InlineData("en-US", "0.500", "5.100")]
    public void ViewFormatsModelUncertaintyAndAgeWithoutClaimingMeasuredError(string locale, string sigma, string age)
    {
        var sample = new TrajectoryEstimateSample(12, new(500200, 7400200, 100), Vector3d.Zero,
            Quaterniond.Identity, new(.3, .4, 0), Vector3d.Zero, 5.1, true, false);
        var view = NavigationTelemetryPresenter.Build(true, sample, "navigation.status.waiting_gnss", locale);
        Assert.True(view.HasEstimate);
        Assert.Equal(sigma, view.Sigma3DMetres);
        Assert.Equal(age, view.GnssAgeSeconds);
        Assert.True(view.BlackoutAlert);
        Assert.False(view.HighUncertaintyAlert);
        Assert.Equal("navigation.status.running", view.StatusKey);
        Assert.DoesNotContain(typeof(NavigationTelemetryView).GetProperties(), p => p.Name.Contains("Error") || p.Name.Contains("Rmse"));
    }

    [Fact]
    public void UnavailableOrUninitializedNavigationHasMissingMetricsInsteadOfZeroes()
    {
        foreach (var hasImu in new[] { false, true })
        {
            var view = NavigationTelemetryPresenter.Build(hasImu, null, "navigation.status.waiting_heading", "en-US");
            Assert.False(view.HasEstimate);
            Assert.Null(view.Sigma3DMetres); Assert.Null(view.GnssAgeSeconds);
            Assert.Equal(hasImu ? "navigation.status.waiting_heading" : "navigation.status.no_imu", view.StatusKey);
        }
    }

    [Fact]
    public void GnssBadgeComesFromObservedSolutionIncludingUnavailableMeasurements()
    {
        var sensor = new GnssSimulator(5);
        var autonomous = sensor.Observe(0, Vector3d.Zero, GnssProfile.Navigation, new(1, Vector3d.Zero), Vector3d.Zero);
        var blackout = sensor.Observe(.2, Vector3d.Zero, GnssProfile.Navigation, new(0, Vector3d.Zero), Vector3d.Zero);
        Assert.Equal("navigation.status.waiting_gnss", NavigationTelemetryPresenter.GnssStatusKey(null));
        Assert.Equal("hud.gnss.autonomous", NavigationTelemetryPresenter.GnssStatusKey(autonomous));
        Assert.Equal("hud.gnss.unavailable", NavigationTelemetryPresenter.GnssStatusKey(blackout));
    }
}
