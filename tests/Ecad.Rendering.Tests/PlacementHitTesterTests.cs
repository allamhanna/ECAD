using Ecad.Rendering.Canvas;
using Xunit;

namespace Ecad.Rendering.Tests;

public class PlacementHitTesterTests
{
    private static readonly CanvasViewport IdentityViewport = new() { PanX = 0, PanY = 0, Zoom = 1.0 };

    [Fact]
    public void HitTest_PointInsidePlacement_ReturnsItsId()
    {
        var placements = new[] { new HitTestPlacement(1, X: 0, Y: 0, Width: 40, Height: 40, RotationDegrees: 0) };

        var hit = PlacementHitTester.HitTest(placements, IdentityViewport, screenX: 20, screenY: 20);

        Assert.Equal(1, hit);
    }

    [Fact]
    public void HitTest_PointOutsideAllPlacements_ReturnsNull()
    {
        var placements = new[] { new HitTestPlacement(1, X: 0, Y: 0, Width: 40, Height: 40, RotationDegrees: 0) };

        var hit = PlacementHitTester.HitTest(placements, IdentityViewport, screenX: 100, screenY: 100);

        Assert.Null(hit);
    }

    [Fact]
    public void HitTest_OverlappingPlacements_ReturnsTopmost()
    {
        var placements = new[]
        {
            new HitTestPlacement(1, X: 0, Y: 0, Width: 40, Height: 40, RotationDegrees: 0),
            new HitTestPlacement(2, X: 0, Y: 0, Width: 40, Height: 40, RotationDegrees: 0), // drawn later -> on top
        };

        var hit = PlacementHitTester.HitTest(placements, IdentityViewport, screenX: 20, screenY: 20);

        Assert.Equal(2, hit);
    }

    [Fact]
    public void HitTest_RotatedPlacement_HitTestsRotatedBounds()
    {
        // A 40x20 placement centered at (20,20), rotated 90 degrees -> effectively occupies a
        // tall 20-wide, 40-high area. A point that would miss the unrotated rect but falls inside
        // the rotated footprint should still hit.
        var placement = new HitTestPlacement(1, X: 0, Y: 0, Width: 40, Height: 20, RotationDegrees: 90);
        var placements = new[] { placement };

        // Center (20,10) before rotation; after rotating 90 degrees the shape spans
        // x in [20-10,20+10]=[10,30], y in [10-20,10+20]=[-10,30] around center (20,10).
        var hit = PlacementHitTester.HitTest(placements, IdentityViewport, screenX: 20, screenY: -5);

        Assert.Equal(1, hit);
    }

    [Fact]
    public void HitTest_AppliesViewportPanAndZoom()
    {
        var placements = new[] { new HitTestPlacement(1, X: 100, Y: 100, Width: 40, Height: 40, RotationDegrees: 0) };
        var viewport = new CanvasViewport { PanX = -100, PanY = -100, Zoom = 2.0 };

        // World point (120,120) is inside the placement; screen point = (120-100)*2 = 40.
        var hit = PlacementHitTester.HitTest(placements, viewport, screenX: 40, screenY: 40);

        Assert.Equal(1, hit);
    }
}
