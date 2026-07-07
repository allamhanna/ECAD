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

        foreach (var (point, connectionIds) in endpointGroups)
        {
            foreach (var connection in connections)
            {
                if (connectionIds.Contains(connection.ConnectionId)) continue;
                if (AutoConnectDetector.IsPointStrictlyOnPolyline(point, connection.Route))
                {
                    junctions.Add(point);
                    break;
                }
            }
        }

        return junctions.ToList();
    }
}
