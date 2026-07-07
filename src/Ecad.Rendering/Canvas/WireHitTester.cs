namespace Ecad.Rendering.Canvas;

/// <summary>A wire's routed path, for hit-testing — deliberately independent of Connection/DevicePin, same spirit as HitTestPlacement.</summary>
public sealed record HitTestWire(long ConnectionId, IReadOnlyList<WorldPoint> Route);

/// <summary>Proximity-based hit-testing for pins and wires (M7) — pins are points, wires are polylines, both need a tolerance since a click is never pixel-perfect.</summary>
public static class WireHitTester
{
    public static long? HitTestPin(WorldPoint point, IReadOnlyList<PinPosition> pins, double tolerance)
    {
        foreach (var pin in pins)
        {
            if (Distance(point, pin.Position) <= tolerance) return pin.DevicePinId;
        }
        return null;
    }

    public static long? HitTestWire(WorldPoint point, IReadOnlyList<HitTestWire> wires, double tolerance)
    {
        foreach (var wire in wires)
        {
            if (IsNearRoute(point, wire.Route, tolerance)) return wire.ConnectionId;
        }
        return null;
    }

    private static bool IsNearRoute(WorldPoint point, IReadOnlyList<WorldPoint> route, double tolerance)
    {
        for (var i = 0; i < route.Count - 1; i++)
        {
            if (DistanceToSegment(point, route[i], route[i + 1]) <= tolerance) return true;
        }
        return false;
    }

    private static double DistanceToSegment(WorldPoint p, WorldPoint a, WorldPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-9) return Distance(p, a);

        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        var projected = new WorldPoint(a.X + t * dx, a.Y + t * dy);
        return Distance(p, projected);
    }

    private static double Distance(WorldPoint a, WorldPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
