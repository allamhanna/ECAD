using Ecad.Rendering.Canvas;
using Xunit;

namespace Ecad.Rendering.Tests;

public class PlacementPinGeometryTests
{
    [Fact]
    public void NoRotation_NoMirror_MapsLocalDirectlyOntoWorld()
    {
        var (x, y) = PlacementPinGeometry.GetPinWorldPosition(0, 0, 0, false, 0, 20);
        Assert.Equal(0, x, 3);
        Assert.Equal(20, y, 3);
    }

    [Fact]
    public void Rotate90_MovesPinAroundCenter()
    {
        var (x, y) = PlacementPinGeometry.GetPinWorldPosition(0, 0, 90, false, 0, 20);
        Assert.Equal(20, x, 3);
        Assert.Equal(0, y, 3);
    }

    [Fact]
    public void Rotate180_MovesPinToOppositeSide()
    {
        var (x, y) = PlacementPinGeometry.GetPinWorldPosition(0, 0, 180, false, 0, 20);
        Assert.Equal(40, x, 3);
        Assert.Equal(20, y, 3);
    }

    [Fact]
    public void Rotate270_MovesPinAroundCenter()
    {
        var (x, y) = PlacementPinGeometry.GetPinWorldPosition(0, 0, 270, false, 0, 20);
        Assert.Equal(20, x, 3);
        Assert.Equal(40, y, 3);
    }

    [Fact]
    public void Mirrored_FlipsAcrossVerticalCenterLine()
    {
        var (x, y) = PlacementPinGeometry.GetPinWorldPosition(0, 0, 0, true, 0, 20);
        Assert.Equal(40, x, 3);
        Assert.Equal(20, y, 3);
    }

    [Fact]
    public void PlacementOffset_TranslatesResult()
    {
        var (x, y) = PlacementPinGeometry.GetPinWorldPosition(100, 200, 0, false, 0, 20);
        Assert.Equal(100, x, 3);
        Assert.Equal(220, y, 3);
    }

    [Theory]
    [InlineData(0, false, 0, 0)]
    [InlineData(90, false, 0, 90)]
    [InlineData(180, false, 0, 180)]
    [InlineData(270, false, 0, 270)]
    [InlineData(0, true, 0, 180)] // mirroring flips left/right
    [InlineData(0, true, 90, 90)] // mirroring a vertical direction leaves it unchanged
    [InlineData(90, true, 0, 270)] // mirror then rotate: 180 - 0 = 180, + 90 = 270
    public void GetPinWorldDirection_TransformsByRotationAndMirror(int rotationDegrees, bool mirrored, double localDirection, double expected)
    {
        var direction = PlacementPinGeometry.GetPinWorldDirection(rotationDegrees, mirrored, localDirection);
        Assert.Equal(expected, direction, 3);
    }
}
