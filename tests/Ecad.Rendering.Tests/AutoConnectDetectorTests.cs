using Ecad.Rendering.Canvas;
using Xunit;

namespace Ecad.Rendering.Tests;

public class AutoConnectDetectorTests
{
    // Direction convention: 0=right, 90=down, 180=left, 270=up.

    [Fact]
    public void FacingPinsOnSameRow_NotAlreadyConnected_AreDetected()
    {
        // A points right at (0,20); B is further right on the same row, pointing back left at it.
        var moved = new[] { new PinPosition(1, new WorldPoint(0, 20), 0) };
        var others = new[] { new PinPosition(2, new WorldPoint(60, 20), 180) };

        var result = AutoConnectDetector.FindNewConnections(moved, others, [], (_, _) => false);

        var pair = Assert.Single(result);
        Assert.Equal((1L, 2L), pair);
    }

    [Fact]
    public void FacingPinsOnSameColumn_AreDetected()
    {
        // A points down at (20,0); B is further down the same column, pointing back up at it.
        var moved = new[] { new PinPosition(1, new WorldPoint(20, 0), 90) };
        var others = new[] { new PinPosition(2, new WorldPoint(20, 60), 270) };

        var result = AutoConnectDetector.FindNewConnections(moved, others, [], (_, _) => false);

        var pair = Assert.Single(result);
        Assert.Equal((1L, 2L), pair);
    }

    [Fact]
    public void SameRow_ButNotFacing_IsNotDetected()
    {
        // Both point right — same row, but B doesn't point back at A.
        var moved = new[] { new PinPosition(1, new WorldPoint(0, 20), 0) };
        var others = new[] { new PinPosition(2, new WorldPoint(60, 20), 0) };

        var result = AutoConnectDetector.FindNewConnections(moved, others, [], (_, _) => false);

        Assert.Empty(result);
    }

    [Fact]
    public void FacingDirections_ButDifferentRow_IsNotDetected()
    {
        var moved = new[] { new PinPosition(1, new WorldPoint(0, 20), 0) };
        var others = new[] { new PinPosition(2, new WorldPoint(60, 40), 180) };

        var result = AutoConnectDetector.FindNewConnections(moved, others, [], (_, _) => false);

        Assert.Empty(result);
    }

    [Fact]
    public void FacingButBehind_IsNotDetected()
    {
        // A points right, but B sits to A's left — not in the direction A is actually pointing.
        var moved = new[] { new PinPosition(1, new WorldPoint(60, 20), 0) };
        var others = new[] { new PinPosition(2, new WorldPoint(0, 20), 180) };

        var result = AutoConnectDetector.FindNewConnections(moved, others, [], (_, _) => false);

        Assert.Empty(result);
    }

    [Fact]
    public void ExactCoincidence_FacingDirections_IsDetected()
    {
        // Two pins at the identical point are just the zero-distance case of "facing" — direction still applies.
        var moved = new[] { new PinPosition(1, new WorldPoint(20, 20), 0) };
        var others = new[] { new PinPosition(2, new WorldPoint(20, 20), 180) };

        var result = AutoConnectDetector.FindNewConnections(moved, others, [], (_, _) => false);

        Assert.Single(result);
    }

    [Fact]
    public void ExactCoincidence_SameDirection_IsNotDetected()
    {
        var moved = new[] { new PinPosition(1, new WorldPoint(20, 20), 0) };
        var others = new[] { new PinPosition(2, new WorldPoint(20, 20), 0) };

        var result = AutoConnectDetector.FindNewConnections(moved, others, [], (_, _) => false);

        Assert.Empty(result);
    }

    [Fact]
    public void FacingPins_AlreadyConnected_IsNotDuplicated()
    {
        var moved = new[] { new PinPosition(1, new WorldPoint(0, 20), 0) };
        var others = new[] { new PinPosition(2, new WorldPoint(60, 20), 180) };

        var result = AutoConnectDetector.FindNewConnections(moved, others, [], (_, _) => true);

        Assert.Empty(result);
    }

    [Fact]
    public void NearestFacingPin_IsChosenOverAFartherOne()
    {
        var moved = new[] { new PinPosition(1, new WorldPoint(0, 20), 0) };
        var others = new[]
        {
            new PinPosition(2, new WorldPoint(100, 20), 180),
            new PinPosition(3, new WorldPoint(40, 20), 180),
        };

        var result = AutoConnectDetector.FindNewConnections(moved, others, [], (_, _) => false);

        var pair = Assert.Single(result);
        Assert.Equal((1L, 3L), pair);
    }

    [Fact]
    public void PinLandingMidSpanOnExistingWire_ConnectsToWireStartPin()
    {
        var moved = new[] { new PinPosition(3, new WorldPoint(20, 0), 0) };
        var existing = new[] { new ExistingConnection(100, 1, 2, [new WorldPoint(0, 0), new WorldPoint(40, 0)]) };

        var result = AutoConnectDetector.FindNewConnections(moved, [], existing, (_, _) => false);

        var pair = Assert.Single(result);
        Assert.Equal((3L, 1L), pair);
    }

    [Fact]
    public void PinAtWireEndpoint_IsNotFlaggedAsMidSpanTouch()
    {
        // Endpoint coincidence is the pin-to-pin case's job, not the mid-span "on wire" case.
        Assert.False(AutoConnectDetector.IsPointStrictlyOnPolyline(new WorldPoint(0, 0), [new WorldPoint(0, 0), new WorldPoint(40, 0)]));
        Assert.False(AutoConnectDetector.IsPointStrictlyOnPolyline(new WorldPoint(40, 0), [new WorldPoint(0, 0), new WorldPoint(40, 0)]));
    }

    [Fact]
    public void PinOffTheWirePath_IsNotFlagged()
    {
        Assert.False(AutoConnectDetector.IsPointStrictlyOnPolyline(new WorldPoint(20, 10), [new WorldPoint(0, 0), new WorldPoint(40, 0)]));
    }
}
