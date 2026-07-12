namespace Ecad.Rendering.Canvas;

/// <summary>A cable line's endpoints, for hit-testing — deliberately independent of Cable/Connection, same spirit as HitTestWire.</summary>
public sealed record HitTestCableLine(long CableLineId, WorldPoint P1, WorldPoint P2);

/// <summary>Proximity-based hit-testing for cable lines (point-to-segment distance) — same shape as
/// WireHitTester's own point-to-polyline test, just for a single fixed segment instead of a routed path.</summary>
public static class CableLineHitTester
{
    public static long? HitTest(WorldPoint point, IReadOnlyList<HitTestCableLine> cableLines, double tolerance)
    {
        foreach (var line in cableLines)
        {
            if (DistanceToSegment(point, line.P1, line.P2) <= tolerance) return line.CableLineId;
        }
        return null;
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
