using Ecad.Rendering.Canvas;
using Xunit;

namespace Ecad.Rendering.Tests;

public class CableLineHitTesterTests
{
    [Fact]
    public void HitTest_NearTheLine_ReturnsItsId()
    {
        var lines = new[] { new HitTestCableLine(100, new WorldPoint(0, 0), new WorldPoint(40, 0)) };

        var hit = CableLineHitTester.HitTest(new WorldPoint(20, 1), lines, tolerance: 3);

        Assert.Equal(100, hit);
    }

    [Fact]
    public void HitTest_FarFromEveryLine_ReturnsNull()
    {
        var lines = new[] { new HitTestCableLine(100, new WorldPoint(0, 0), new WorldPoint(40, 0)) };

        var hit = CableLineHitTester.HitTest(new WorldPoint(20, 30), lines, tolerance: 3);

        Assert.Null(hit);
    }

    [Fact]
    public void HitTest_TwoLinesWithinTolerance_ReturnsWhicheverIsHit()
    {
        var lines = new[]
        {
            new HitTestCableLine(1, new WorldPoint(0, 0), new WorldPoint(40, 0)),
            new HitTestCableLine(2, new WorldPoint(0, 20), new WorldPoint(40, 20)),
        };

        var hit = CableLineHitTester.HitTest(new WorldPoint(20, 19), lines, tolerance: 3);

        Assert.Equal(2, hit);
    }
}
