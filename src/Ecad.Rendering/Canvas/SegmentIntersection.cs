namespace Ecad.Rendering.Canvas;

/// <summary>
/// Segment/segment and segment/polyline intersection — the one geometric primitive this codebase didn't
/// already have (RouteMath/WireHitTester are all point-to-polyline proximity, never crossing tests).
/// Used by the cable-definition-line feature to detect which wires a drawn line crosses.
/// </summary>
public static class SegmentIntersection
{
    /// <summary>The intersection point of two line SEGMENTS (not infinite lines), or null if they're
    /// parallel or the intersection falls outside either segment's own [0,1] range.</summary>
    public static WorldPoint? Intersect(WorldPoint a1, WorldPoint a2, WorldPoint b1, WorldPoint b2)
    {
        var rX = a2.X - a1.X;
        var rY = a2.Y - a1.Y;
        var sX = b2.X - b1.X;
        var sY = b2.Y - b1.Y;

        var denominator = rX * sY - rY * sX;
        if (Math.Abs(denominator) < 1e-9) return null; // parallel (or either segment is a point)

        var qpX = b1.X - a1.X;
        var qpY = b1.Y - a1.Y;

        var t = (qpX * sY - qpY * sX) / denominator;
        var u = (qpX * rY - qpY * rX) / denominator;

        if (t is < 0 or > 1 || u is < 0 or > 1) return null;

        return new WorldPoint(a1.X + t * rX, a1.Y + t * rY);
    }

    /// <summary>Tests a drawn line segment against every segment of a wire's route (1 segment if
    /// straight, 2 if OrthogonalRouter's one-bend shape), returning the first intersection found.</summary>
    public static WorldPoint? IntersectRoute(WorldPoint lineStart, WorldPoint lineEnd, IReadOnlyList<WorldPoint> route)
    {
        for (var i = 0; i < route.Count - 1; i++)
        {
            var hit = Intersect(lineStart, lineEnd, route[i], route[i + 1]);
            if (hit is not null) return hit;
        }
        return null;
    }
}
