namespace Ecad.Rendering.Canvas;

/// <summary>The subset of a placement's geometry needed for hit-testing — deliberately independent of the Ecad.Core Placement model so this stays a pure, dependency-free rendering concern.</summary>
public sealed record HitTestPlacement(long Id, double X, double Y, double Width, double Height, int RotationDegrees);

public static class PlacementHitTester
{
    /// <summary>Returns the topmost (last-drawn) placement under the given screen point, or null if none. Accounts for rotation around each placement's center.</summary>
    public static long? HitTest(IReadOnlyList<HitTestPlacement> placements, CanvasViewport viewport, double screenX, double screenY)
    {
        var (worldX, worldY) = viewport.ScreenToWorld(screenX, screenY);

        for (var i = placements.Count - 1; i >= 0; i--)
        {
            if (IsPointInRotatedRect(worldX, worldY, placements[i])) return placements[i].Id;
        }
        return null;
    }

    /// <summary>Every placement whose (unrotated) bounding box intersects the given world-space
    /// rectangle — the rubber-band multi-select query. Deliberately not rotation-aware (unlike the
    /// point-based HitTest above): a precise rotated-rect-vs-rect intersection isn't worth the
    /// complexity for a marquee select, where "roughly overlaps" is what a user expects anyway.
    /// requireFullContainment switches to the AutoCAD-style "window select" rule (a placement must be
    /// entirely inside the rectangle, not just touched by it) — the caller picks this based on drag
    /// direction (left-to-right = window, right-to-left = crossing).</summary>
    public static IReadOnlyList<long> HitTestRect(IReadOnlyList<HitTestPlacement> placements, double worldX1, double worldY1, double worldX2, double worldY2,
        bool requireFullContainment = false)
    {
        var minX = Math.Min(worldX1, worldX2);
        var maxX = Math.Max(worldX1, worldX2);
        var minY = Math.Min(worldY1, worldY2);
        var maxY = Math.Max(worldY1, worldY2);

        var result = new List<long>();
        foreach (var placement in placements)
        {
            var placementMaxX = placement.X + placement.Width;
            var placementMaxY = placement.Y + placement.Height;

            var hit = requireFullContainment
                ? placement.X >= minX && placementMaxX <= maxX && placement.Y >= minY && placementMaxY <= maxY
                : placementMaxX >= minX && placement.X <= maxX && placementMaxY >= minY && placement.Y <= maxY;

            if (hit) result.Add(placement.Id);
        }
        return result;
    }

    private static bool IsPointInRotatedRect(double pointX, double pointY, HitTestPlacement placement)
    {
        var centerX = placement.X + placement.Width / 2;
        var centerY = placement.Y + placement.Height / 2;

        // Rotate the point by -rotation around the placement's center, then it's a plain axis-aligned bounds check.
        var angleRad = -placement.RotationDegrees * Math.PI / 180.0;
        var dx = pointX - centerX;
        var dy = pointY - centerY;
        var localX = dx * Math.Cos(angleRad) - dy * Math.Sin(angleRad);
        var localY = dx * Math.Sin(angleRad) + dy * Math.Cos(angleRad);

        return Math.Abs(localX) <= placement.Width / 2 && Math.Abs(localY) <= placement.Height / 2;
    }
}
