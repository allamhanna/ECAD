using Ecad.Rendering.Canvas;
using Xunit;

namespace Ecad.Rendering.Tests;

public class SegmentIntersectionTests
{
    [Fact]
    public void Intersect_CrossingSegments_ReturnsThePoint()
    {
        // A vertical line x=20 crossing a horizontal line y=10, both within range.
        var hit = SegmentIntersection.Intersect(new WorldPoint(20, 0), new WorldPoint(20, 20), new WorldPoint(0, 10), new WorldPoint(40, 10));

        Assert.NotNull(hit);
        Assert.Equal(20, hit!.Value.X, precision: 6);
        Assert.Equal(10, hit.Value.Y, precision: 6);
    }

    [Fact]
    public void Intersect_ParallelSegments_ReturnsNull()
    {
        var hit = SegmentIntersection.Intersect(new WorldPoint(0, 0), new WorldPoint(40, 0), new WorldPoint(0, 10), new WorldPoint(40, 10));

        Assert.Null(hit);
    }

    [Fact]
    public void Intersect_LinesWouldCrossButSegmentsDoNotReach_ReturnsNull()
    {
        // Same lines as the crossing case, but the vertical segment stops short of y=10.
        var hit = SegmentIntersection.Intersect(new WorldPoint(20, 0), new WorldPoint(20, 5), new WorldPoint(0, 10), new WorldPoint(40, 10));

        Assert.Null(hit);
    }

    [Fact]
    public void Intersect_TouchingAtASharedEndpoint_ReturnsThatPoint()
    {
        var hit = SegmentIntersection.Intersect(new WorldPoint(0, 0), new WorldPoint(20, 0), new WorldPoint(20, 0), new WorldPoint(20, 20));

        Assert.NotNull(hit);
        Assert.Equal(new WorldPoint(20, 0), hit!.Value);
    }

    [Fact]
    public void IntersectRoute_StraightRoute_FindsTheCrossing()
    {
        var route = new[] { new WorldPoint(0, 0), new WorldPoint(40, 0) };

        var hit = SegmentIntersection.IntersectRoute(new WorldPoint(20, -10), new WorldPoint(20, 10), route);

        Assert.NotNull(hit);
        Assert.Equal(20, hit!.Value.X, precision: 6);
        Assert.Equal(0, hit.Value.Y, precision: 6);
    }

    [Fact]
    public void IntersectRoute_BentRoute_FindsTheCrossingOnTheSecondSegment()
    {
        // Bend at (40,0) -> (40,30); a horizontal probe at y=15 only crosses the second segment.
        var route = new[] { new WorldPoint(0, 0), new WorldPoint(40, 0), new WorldPoint(40, 30) };

        var hit = SegmentIntersection.IntersectRoute(new WorldPoint(20, 15), new WorldPoint(60, 15), route);

        Assert.NotNull(hit);
        Assert.Equal(40, hit!.Value.X, precision: 6);
        Assert.Equal(15, hit.Value.Y, precision: 6);
    }

    [Fact]
    public void IntersectRoute_NoSegmentCrossed_ReturnsNull()
    {
        var route = new[] { new WorldPoint(0, 0), new WorldPoint(40, 0) };

        var hit = SegmentIntersection.IntersectRoute(new WorldPoint(20, 10), new WorldPoint(20, 20), route);

        Assert.Null(hit);
    }
}
