namespace Ecad.Rendering.Canvas;

/// <summary>
/// Pure detection of where a junction dot belongs (Section 6.1: "Junctions/T-nodes supported") —
/// either 3+ distinct connections sharing one endpoint, or one connection's endpoint landing strictly
/// inside another connection's routed path. No separate junction entity exists in the schema; a
/// junction is purely a rendering signal derived from the existing pairwise Connection records.
/// </summary>
public static class JunctionDetector
{
    public static IReadOnlyList<WorldPoint> FindJunctions(IReadOnlyList<ExistingConnection> connections, IReadOnlyList<PinPosition> pinPositions)
    {
        var pinPositionById = pinPositions.ToDictionary(p => p.DevicePinId, p => p.Position);
        var endpointGroups = new Dictionary<WorldPoint, HashSet<long>>();

        void RegisterEndpoint(WorldPoint point, long connectionId)
        {
            if (!endpointGroups.TryGetValue(point, out var set))
            {
                set = [];
                endpointGroups[point] = set;
            }
            set.Add(connectionId);
        }

        foreach (var connection in connections)
        {
            if (pinPositionById.TryGetValue(connection.FromDevicePinId, out var fromPos)) RegisterEndpoint(fromPos, connection.ConnectionId);
            if (pinPositionById.TryGetValue(connection.ToDevicePinId, out var toPos)) RegisterEndpoint(toPos, connection.ConnectionId);
        }

        var junctions = new HashSet<WorldPoint>();

        foreach (var (point, connectionIds) in endpointGroups)
        {
            if (connectionIds.Count >= 3) junctions.Add(point);
        }

        // T-junctions: a point landing strictly inside another connection's routed path. Checking every
        // endpoint against every connection's route is O(endpoints x connections) — quadratic, and the
        // dominant rebuild cost on any page with a few hundred wires (M14 perf pass). Every
        // OrthogonalRouter route is horizontal/vertical-only, so a segment can only ever contain a point
        // that shares its exact Y (horizontal) or X (vertical) coordinate — indexing segments by that
        // coordinate first means each endpoint only re-checks the handful of segments that could
        // possibly contain it, not every connection in the project. The actual containment test is
        // still the same AutoConnectDetector.IsPointStrictlyOnPolyline used before, just against a
        // filtered candidate list instead of everything.
        var horizontalSegments = new Dictionary<double, List<(WorldPoint A, WorldPoint B)>>();
        var verticalSegments = new Dictionary<double, List<(WorldPoint A, WorldPoint B)>>();
        var diagonalSegments = new List<(WorldPoint A, WorldPoint B)>();

        foreach (var connection in connections)
        {
            for (var i = 0; i < connection.Route.Count - 1; i++)
            {
                var a = connection.Route[i];
                var b = connection.Route[i + 1];
                if (a.Y == b.Y)
                {
                    if (!horizontalSegments.TryGetValue(a.Y, out var segments)) horizontalSegments[a.Y] = segments = [];
                    segments.Add((a, b));
                }
                else if (a.X == b.X)
                {
                    if (!verticalSegments.TryGetValue(a.X, out var segments)) verticalSegments[a.X] = segments = [];
                    segments.Add((a, b));
                }
                else
                {
                    diagonalSegments.Add((a, b));
                }
            }
        }

        foreach (var point in endpointGroups.Keys)
        {
            var isJunction = false;

            if (horizontalSegments.TryGetValue(point.Y, out var hSegments))
                isJunction = hSegments.Any(s => AutoConnectDetector.IsPointStrictlyOnPolyline(point, [s.A, s.B]));

            if (!isJunction && verticalSegments.TryGetValue(point.X, out var vSegments))
                isJunction = vSegments.Any(s => AutoConnectDetector.IsPointStrictlyOnPolyline(point, [s.A, s.B]));

            if (!isJunction && diagonalSegments.Count > 0)
                isJunction = diagonalSegments.Any(s => AutoConnectDetector.IsPointStrictlyOnPolyline(point, [s.A, s.B]));

            if (isJunction) junctions.Add(point);
        }

        return junctions.ToList();
    }
}
