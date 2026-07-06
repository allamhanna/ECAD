using Ecad.Rendering.Canvas;
using Xunit;

namespace Ecad.Rendering.Tests;

public class CanvasViewportTests
{
    [Fact]
    public void WorldToScreen_ThenScreenToWorld_RoundTrips()
    {
        var viewport = new CanvasViewport { PanX = 10, PanY = -5, Zoom = 2.0 };

        var (screenX, screenY) = viewport.WorldToScreen(30, 40);
        var (worldX, worldY) = viewport.ScreenToWorld(screenX, screenY);

        Assert.Equal(30, worldX, 3);
        Assert.Equal(40, worldY, 3);
    }

    [Fact]
    public void WorldToScreen_AppliesPanAndZoom()
    {
        var viewport = new CanvasViewport { PanX = 10, PanY = 0, Zoom = 2.0 };

        var (screenX, screenY) = viewport.WorldToScreen(0, 0);

        Assert.Equal(20, screenX); // (0 + 10) * 2
        Assert.Equal(0, screenY);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(11, 11, 20, 20)] // grid spacing 20 -> rounds to nearest multiple
    [InlineData(-11, -11, -20, -20)]
    [InlineData(9, 9, 0, 0)]
    public void SnapToGrid_RoundsToNearestGridMultiple(double worldX, double worldY, double expectedX, double expectedY)
    {
        var viewport = new CanvasViewport { GridSpacing = 20 };

        var (snappedX, snappedY) = viewport.SnapToGrid(worldX, worldY);

        Assert.Equal(expectedX, snappedX);
        Assert.Equal(expectedY, snappedY);
    }
}
