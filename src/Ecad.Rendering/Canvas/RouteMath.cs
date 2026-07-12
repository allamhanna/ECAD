namespace Ecad.Rendering.Canvas;

/// <summary>
/// Arc-length parametrization over a wire's route (2 points for a straight run, 3 for OrthogonalRouter's
/// single bend — never stored, always recomputed live from current pin positions). A connection
/// definition point is stored as a T fraction (0..1) of the route's total length rather than an absolute
/// point, specifically so it stays sensibly placed on the line after the route re-shapes itself (a
/// placement moves, the bend's corner shifts, etc.) instead of floating in space at a stale coordinate.
/// </summary>
public static class RouteMath
{
    /// <summary>The point at the given fraction (0..1, clamped) of the route's total length. A route
    /// with fewer than 2 points or zero total length just returns its first point (nothing to
    /// interpolate along).</summary>
    public static WorldPoint PointAtT(IReadOnlyList<WorldPoint> route, double t)
    {
        if (route.Count == 0) return default;
        if (route.Count == 1) return route[0];

        var clampedT = Math.Clamp(t, 0.0, 1.0);
        var totalLength = TotalLength(route);
        if (totalLength <= 0) return route[0];

        var targetDistance = clampedT * totalLength;
        var distanceSoFar = 0.0;

        for (var i = 0; i < route.Count - 1; i++)
        {
            var segmentLength = Distance(route[i], route[i + 1]);
            if (distanceSoFar + segmentLength >= targetDistance)
            {
                var segmentT = segmentLength <= 0 ? 0 : (targetDistance - distanceSoFar) / segmentLength;
                return Lerp(route[i], route[i + 1], segmentT);
            }
            distanceSoFar += segmentLength;
        }

        return route[^1];
    }

    /// <summary>The nearest point on the route to the given world point, expressed as a length-fraction
    /// T (0..1) plus the perpendicular distance from the point to that nearest spot on the line — the
    /// distance is what a caller uses to decide "was this click actually near this wire" (a hit-testing
    /// tolerance check), the T is what gets stored as the definition point's position.</summary>
    public static (double T, double Distance) ProjectToT(IReadOnlyList<WorldPoint> route, WorldPoint point)
    {
        if (route.Count == 0) return (0, double.MaxValue);
        if (route.Count == 1) return (0, Distance(route[0], point));

        var totalLength = TotalLength(route);
        if (totalLength <= 0) return (0, Distance(route[0], point));

        var distanceSoFar = 0.0;
        double? bestDistance = null;
        var bestT = 0.0;

        for (var i = 0; i < route.Count - 1; i++)
        {
            var segmentStart = route[i];
            var segmentEnd = route[i + 1];
            var segmentLength = Distance(segmentStart, segmentEnd);
            var (localT, distance) = ProjectToSegment(segmentStart, segmentEnd, point);

            if (bestDistance is null || distance < bestDistance)
            {
                bestDistance = distance;
                bestT = (distanceSoFar + localT * segmentLength) / totalLength;
            }
            distanceSoFar += segmentLength;
        }

        return (bestT, bestDistance ?? double.MaxValue);
    }

    private static double TotalLength(IReadOnlyList<WorldPoint> route)
    {
        var total = 0.0;
        for (var i = 0; i < route.Count - 1; i++) total += Distance(route[i], route[i + 1]);
        return total;
    }

    private static (double LocalT, double Distance) ProjectToSegment(WorldPoint start, WorldPoint end, WorldPoint point)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;

        var localT = lengthSquared <= 0 ? 0 : Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0.0, 1.0);
        var closest = Lerp(start, end, localT);
        return (localT, Distance(closest, point));
    }

    private static WorldPoint Lerp(WorldPoint a, WorldPoint b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static double Distance(WorldPoint a, WorldPoint b) =>
        Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
}
