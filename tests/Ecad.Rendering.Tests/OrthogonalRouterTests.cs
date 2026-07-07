using Ecad.Rendering.Canvas;
using Xunit;

namespace Ecad.Rendering.Tests;

public class OrthogonalRouterTests
{
    [Fact]
    public void SameX_RoutesAsStraightVerticalLine()
    {
        var route = OrthogonalRouter.Route(new WorldPoint(10, 0), new WorldPoint(10, 40));
        Assert.Equal([new WorldPoint(10, 0), new WorldPoint(10, 40)], route);
    }

    [Fact]
    public void SameY_RoutesAsStraightHorizontalLine()
    {
        var route = OrthogonalRouter.Route(new WorldPoint(0, 10), new WorldPoint(40, 10));
        Assert.Equal([new WorldPoint(0, 10), new WorldPoint(40, 10)], route);
    }

    [Fact]
    public void DifferentXAndY_RoutesWithOneBend()
    {
        var route = OrthogonalRouter.Route(new WorldPoint(0, 0), new WorldPoint(40, 40));
        Assert.Equal([new WorldPoint(0, 0), new WorldPoint(40, 0), new WorldPoint(40, 40)], route);
    }
}
