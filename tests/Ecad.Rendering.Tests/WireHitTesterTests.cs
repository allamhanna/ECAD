using Ecad.Rendering.Canvas;
using Xunit;

namespace Ecad.Rendering.Tests;

public class WireHitTesterTests
{
    [Fact]
    public void HitTestPin_WithinTolerance_ReturnsPin()
    {
        var pins = new[] { new PinPosition(1, new WorldPoint(20, 20), 0) };

        var hit = WireHitTester.HitTestPin(new WorldPoint(21, 21), pins, tolerance: 3);

        Assert.Equal(1, hit);
    }

    [Fact]
    public void HitTestPin_OutsideTolerance_ReturnsNull()
    {
        var pins = new[] { new PinPosition(1, new WorldPoint(20, 20), 0) };

        var hit = WireHitTester.HitTestPin(new WorldPoint(40, 40), pins, tolerance: 3);

        Assert.Null(hit);
    }

    [Fact]
    public void HitTestWire_NearStraightSegment_ReturnsConnection()
    {
        var wires = new[] { new HitTestWire(100, [new WorldPoint(0, 0), new WorldPoint(40, 0)]) };

        var hit = WireHitTester.HitTestWire(new WorldPoint(20, 1), wires, tolerance: 3);

        Assert.Equal(100, hit);
    }

    [Fact]
    public void HitTestWire_NearLShapedBend_ReturnsConnection()
    {
        var wires = new[] { new HitTestWire(100, [new WorldPoint(0, 0), new WorldPoint(40, 0), new WorldPoint(40, 40)]) };

        var hit = WireHitTester.HitTestWire(new WorldPoint(41, 20), wires, tolerance: 3);

        Assert.Equal(100, hit);
    }

    [Fact]
    public void HitTestWire_FarFromPath_ReturnsNull()
    {
        var wires = new[] { new HitTestWire(100, [new WorldPoint(0, 0), new WorldPoint(40, 0)]) };

        var hit = WireHitTester.HitTestWire(new WorldPoint(20, 30), wires, tolerance: 3);

        Assert.Null(hit);
    }
}
