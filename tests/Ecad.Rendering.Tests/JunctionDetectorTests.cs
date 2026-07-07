using Ecad.Rendering.Canvas;
using Xunit;

namespace Ecad.Rendering.Tests;

public class JunctionDetectorTests
{
    [Fact]
    public void ThreeConnectionsSharingOneEndpoint_IsAJunction()
    {
        var pins = new[]
        {
            new PinPosition(1, new WorldPoint(20, 20), 0),
            new PinPosition(2, new WorldPoint(0, 0), 0),
            new PinPosition(3, new WorldPoint(40, 0), 0),
            new PinPosition(4, new WorldPoint(20, 40), 0),
        };
        var connections = new[]
        {
            new ExistingConnection(100, 1, 2, [new WorldPoint(20, 20), new WorldPoint(0, 0)]),
            new ExistingConnection(101, 1, 3, [new WorldPoint(20, 20), new WorldPoint(40, 0)]),
            new ExistingConnection(102, 1, 4, [new WorldPoint(20, 20), new WorldPoint(20, 40)]),
        };

        var junctions = JunctionDetector.FindJunctions(connections, pins);

        Assert.Contains(new WorldPoint(20, 20), junctions);
    }

    [Fact]
    public void TwoUnrelatedConnections_NoJunction()
    {
        var pins = new[]
        {
            new PinPosition(1, new WorldPoint(0, 0), 0),
            new PinPosition(2, new WorldPoint(40, 0), 0),
            new PinPosition(3, new WorldPoint(0, 100), 0),
            new PinPosition(4, new WorldPoint(40, 100), 0),
        };
        var connections = new[]
        {
            new ExistingConnection(100, 1, 2, [new WorldPoint(0, 0), new WorldPoint(40, 0)]),
            new ExistingConnection(101, 3, 4, [new WorldPoint(0, 100), new WorldPoint(40, 100)]),
        };

        var junctions = JunctionDetector.FindJunctions(connections, pins);

        Assert.Empty(junctions);
    }

    [Fact]
    public void EndpointLandingInsideAnotherConnectionsSegment_IsAJunction()
    {
        var pins = new[]
        {
            new PinPosition(1, new WorldPoint(0, 0), 0),
            new PinPosition(2, new WorldPoint(40, 0), 0),
            new PinPosition(3, new WorldPoint(20, 0), 0),
            new PinPosition(4, new WorldPoint(20, 40), 0),
        };
        var connections = new[]
        {
            new ExistingConnection(100, 1, 2, [new WorldPoint(0, 0), new WorldPoint(40, 0)]),
            new ExistingConnection(101, 3, 4, [new WorldPoint(20, 0), new WorldPoint(20, 40)]),
        };

        var junctions = JunctionDetector.FindJunctions(connections, pins);

        Assert.Contains(new WorldPoint(20, 0), junctions);
    }
}
