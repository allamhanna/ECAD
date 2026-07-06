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
