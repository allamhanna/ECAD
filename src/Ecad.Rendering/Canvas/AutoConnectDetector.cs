namespace Ecad.Rendering.Canvas;

/// <summary>
/// Pure detection of auto-connectable pins (Section 5.5/6.1: "auto-detected from touching symbol
/// pins"). Two pins connect only if they sit on the same grid line AND their outward directions
/// point toward each other — a coil pin exiting right only joins another pin on the same row exiting
/// left back toward it, not just any pin that happens to share that row or happen to touch it.
/// Exact coincidence (two pins at the identical point) is just the zero-distance case of this same
/// rule, not a separate check — direction still has to match. Also detects a pin landing strictly
/// inside an existing connection's routed path (a mid-span "T" touch), independent of direction since
/// a wire segment has no facing of its own. Takes already-computed positions/routes so it has no
/// knowledge of symbols, placements, or the database — the caller (SchematicPageViewModel) resolves
/// geometry first, then hands it here, then persists whatever pairs come back.
/// </summary>
public static class AutoConnectDetector
{
    private const double Epsilon = 0.01;

    public static IReadOnlyList<(long FromDevicePinId, long ToDevicePinId)> FindNewConnections(
        IReadOnlyList<PinPosition> movedPins,
        IReadOnlyList<PinPosition> otherPins,
        IReadOnlyList<ExistingConnection> existingConnections,
        Func<long, long, bool> areAlreadyConnected)
    {
        var results = new List<(long, long)>();

        foreach (var movedPin in movedPins)
        {
            PinPosition? nearestFacing = null;
            var nearestDistance = double.MaxValue;

            foreach (var otherPin in otherPins)
            {
                if (otherPin.DevicePinId == movedPin.DevicePinId) continue;
                if (!AreFacingEachOther(movedPin, otherPin)) continue;
                if (areAlreadyConnected(movedPin.DevicePinId, otherPin.DevicePinId)) continue;

                var distance = Distance(movedPin.Position, otherPin.Position);
                if (distance >= nearestDistance) continue;

                nearestDistance = distance;
                nearestFacing = otherPin;
            }

            if (nearestFacing is not null)
                results.Add((movedPin.DevicePinId, nearestFacing.DevicePinId));

            foreach (var connection in existingConnections)
            {
                if (connection.FromDevicePinId == movedPin.DevicePinId || connection.ToDevicePinId == movedPin.DevicePinId) continue;
                if (!IsPointStrictlyOnPolyline(movedPin.Position, connection.Route)) continue;
                if (areAlreadyConnected(movedPin.DevicePinId, connection.FromDevicePinId)) continue;
                results.Add((movedPin.DevicePinId, connection.FromDevicePinId));
            }
        }

        return results;
    }

    /// <summary>True if a and b sit on the same grid line, in the direction a is pointing, and b points directly back at a.</summary>
    public static bool AreFacingEachOther(PinPosition a, PinPosition b)
    {
        if (!AreOppositeDirections(a.Direction, b.Direction)) return false;

        return NormalizeAngle(a.Direction) switch
        {
            0 => IsClose(a.Position.Y, b.Position.Y) && b.Position.X >= a.Position.X,
            180 => IsClose(a.Position.Y, b.Position.Y) && b.Position.X <= a.Position.X,
            90 => IsClose(a.Position.X, b.Position.X) && b.Position.Y >= a.Position.Y,
            270 => IsClose(a.Position.X, b.Position.X) && b.Position.Y <= a.Position.Y,
            _ => false, // only the four orthogonal directions are meaningful for this grid-based model
        };
    }

    /// <summary>Exposed so the ViewModel's magnet-snap can pull a dragged pin only toward pins it could actually connect to, not just any nearby pin.</summary>
    public static bool AreOppositeDirections(double directionA, double directionB) =>
        IsClose(NormalizeAngle(directionA + 180), NormalizeAngle(directionB));

    private static double NormalizeAngle(double angle)
    {
        var normalized = angle % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static bool IsClose(double a, double b) => Math.Abs(a - b) < Epsilon;

    private static double Distance(WorldPoint a, WorldPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    public static bool IsPointStrictlyOnPolyline(WorldPoint point, IReadOnlyList<WorldPoint> polyline)
    {
        for (var i = 0; i < polyline.Count - 1; i++)
        {
            if (IsPointStrictlyOnSegment(point, polyline[i], polyline[i + 1])) return true;
        }
        return false;
    }

    private static bool IsPointStrictlyOnSegment(WorldPoint p, WorldPoint a, WorldPoint b)
    {
        var cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
        if (Math.Abs(cross) > Epsilon) return false;

        var dot = (p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y);
        var lengthSquared = (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y);
        if (dot <= Epsilon || dot >= lengthSquared - Epsilon) return false;

        return true;
    }
}
