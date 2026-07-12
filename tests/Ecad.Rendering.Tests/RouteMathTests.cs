using Ecad.Rendering.Canvas;
using Xunit;

namespace Ecad.Rendering.Tests;

public class RouteMathTests
{
    private static readonly WorldPoint[] StraightRoute = [new WorldPoint(0, 0), new WorldPoint(100, 0)];

    // One bend: (0,0) -> (40,0) -> (40,30) — segment lengths 40 and 30, total 70.
    private static readonly WorldPoint[] BentRoute = [new WorldPoint(0, 0), new WorldPoint(40, 0), new WorldPoint(40, 30)];

    [Theory]
    [InlineData(0.0, 0, 0)]
    [InlineData(0.5, 50, 0)]
    [InlineData(1.0, 100, 0)]
    public void PointAtT_StraightRoute_InterpolatesAlongTheSingleSegment(double t, double expectedX, double expectedY)
    {
        var point = RouteMath.PointAtT(StraightRoute, t);

        Assert.Equal(expectedX, point.X, precision: 6);
        Assert.Equal(expectedY, point.Y, precision: 6);
    }

    [Fact]
    public void PointAtT_BentRoute_AtStartAndEnd_ReturnsRouteEndpoints()
    {
        Assert.Equal(new WorldPoint(0, 0), RouteMath.PointAtT(BentRoute, 0.0));
        Assert.Equal(new WorldPoint(40, 30), RouteMath.PointAtT(BentRoute, 1.0));
    }

    [Fact]
    public void PointAtT_BentRoute_AtTheBendFraction_ReturnsTheCorner()
    {
        var bendT = 40.0 / 70.0;

        var point = RouteMath.PointAtT(BentRoute, bendT);

        Assert.Equal(40, point.X, precision: 6);
        Assert.Equal(0, point.Y, precision: 6);
    }

    [Fact]
    public void PointAtT_BentRoute_ArbitraryFractionPastTheBend_InterpolatesOnTheSecondSegment()
    {
        // t=0.9 -> distance 63 along a 70-length route: 40 into the first segment, 23 into the second.
        var point = RouteMath.PointAtT(BentRoute, 0.9);

        Assert.Equal(40, point.X, precision: 6);
        Assert.Equal(23, point.Y, precision: 6);
    }

    [Fact]
    public void ProjectToT_PointOnTheStraightRoute_RoundTripsToItsKnownTAndZeroDistance()
    {
        var (t, distance) = RouteMath.ProjectToT(StraightRoute, new WorldPoint(25, 0));

        Assert.Equal(0.25, t, precision: 6);
        Assert.Equal(0, distance, precision: 6);
    }

    [Fact]
    public void ProjectToT_PointOffTheRoute_ReturnsThePerpendicularDistance()
    {
        var (t, distance) = RouteMath.ProjectToT(StraightRoute, new WorldPoint(50, 5));

        Assert.Equal(0.5, t, precision: 6);
        Assert.Equal(5, distance, precision: 6);
    }

    [Fact]
    public void ProjectToT_PointAtTheBendCorner_RoundTripsToTheBendFraction()
    {
        var (t, distance) = RouteMath.ProjectToT(BentRoute, new WorldPoint(40, 0));

        Assert.Equal(40.0 / 70.0, t, precision: 6);
        Assert.Equal(0, distance, precision: 6);
    }
}
