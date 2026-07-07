using Dapper;
using Ecad.Core.Enums;
using Ecad.Core.Models;
using Microsoft.Data.Sqlite;

namespace Ecad.Data.Repositories;

public class ConnectionRepository(SqliteConnection connection)
{
    public long InsertConnection(Connection conn)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO Connection (FromDevicePinId, ToDevicePinId, WireNumber, Color, CrossSectionMm2, LengthMm, PartId, CableId, CableCoreId)
            VALUES (@FromDevicePinId, @ToDevicePinId, @WireNumber, @Color, @CrossSectionMm2, @LengthMm, @PartId, @CableId, @CableCoreId)
            RETURNING Id;
            """,
            conn);
    }

    public Connection? GetConnection(long id)
    {
        return connection.QuerySingleOrDefault<Connection>("SELECT * FROM Connection WHERE Id = @id;", new { id });
    }

    /// <summary>Deletes just this Connection row; ConnectionEnd rows cascade.</summary>
    public void DeleteConnection(long connectionId)
    {
        connection.Execute("DELETE FROM Connection WHERE Id = @connectionId;", new { connectionId });
    }

    /// <summary>Every Connection whose endpoints both resolve to a Placement on this page — the only
    /// connections this milestone can render or interact with (see ADR-009: cross-page wiring is deferred).</summary>
    public IReadOnlyList<Connection> GetConnectionsForPage(long pageId)
    {
        return connection.Query<Connection>(
            """
            SELECT c.*
            FROM Connection c
            JOIN PlacementPin fromPP ON fromPP.DevicePinId = c.FromDevicePinId
            JOIN Placement fromP ON fromP.Id = fromPP.PlacementId
            JOIN PlacementPin toPP ON toPP.DevicePinId = c.ToDevicePinId
            JOIN Placement toP ON toP.Id = toPP.PlacementId
            WHERE fromP.PageId = @pageId AND toP.PageId = @pageId
            ORDER BY c.Id;
            """,
            new { pageId }).ToList();
    }

    /// <summary>Every Connection touching this DevicePin, in either direction — used to clean up
    /// dependent Connections before a placement's exclusive DevicePins are deleted (M7: Connection's
    /// FKs to DevicePin have no ON DELETE CASCADE, so this must run first or the delete throws).</summary>
    public IReadOnlyList<Connection> GetConnectionsForDevicePin(long devicePinId)
    {
        return connection.Query<Connection>(
            "SELECT * FROM Connection WHERE FromDevicePinId = @devicePinId OR ToDevicePinId = @devicePinId;",
            new { devicePinId }).ToList();
    }

    public bool AreConnected(long devicePinIdA, long devicePinIdB)
    {
        var count = connection.ExecuteScalar<long>(
            """
            SELECT COUNT(*) FROM Connection
            WHERE (FromDevicePinId = @devicePinIdA AND ToDevicePinId = @devicePinIdB)
               OR (FromDevicePinId = @devicePinIdB AND ToDevicePinId = @devicePinIdA);
            """,
            new { devicePinIdA, devicePinIdB });
        return count > 0;
    }

    public void UpdateWireNumber(long connectionId, string? wireNumber)
    {
        connection.Execute("UPDATE Connection SET WireNumber = @wireNumber WHERE Id = @connectionId;", new { connectionId, wireNumber });
    }

    /// <summary>Exact-match lookup for wire-number-uniqueness checks, excluding a given Connection (for rename-in-place) — same pattern as DeviceRepository.FindByTag.</summary>
    public Connection? FindByWireNumber(long projectId, string wireNumber, long? excludingConnectionId)
    {
        return connection.QuerySingleOrDefault<Connection>(
            """
            SELECT c.* FROM Connection c
            JOIN DevicePin dp ON dp.Id = c.FromDevicePinId
            JOIN Device d ON d.Id = dp.DeviceId
            WHERE d.ProjectId = @projectId
              AND c.WireNumber = @wireNumber
              AND (@excludingConnectionId IS NULL OR c.Id != @excludingConnectionId)
            LIMIT 1;
            """,
            new { projectId, wireNumber, excludingConnectionId });
    }

    /// <summary>All assigned wire numbers in this project (via each connection's From pin's Device) — used for uniqueness checks and auto-numbering suggestions.</summary>
    public IReadOnlyList<string> GetAllWireNumbers(long projectId)
    {
        return connection.Query<string>(
            """
            SELECT c.WireNumber
            FROM Connection c
            JOIN DevicePin dp ON dp.Id = c.FromDevicePinId
            JOIN Device d ON d.Id = dp.DeviceId
            WHERE d.ProjectId = @projectId AND c.WireNumber IS NOT NULL;
            """,
            new { projectId }).ToList();
    }

    /// <summary>Every Connection in the project, ordered by its From pin's page SortOrder then Id — the deterministic order "Renumber Wires" assigns fresh sequential numbers in.</summary>
    public IReadOnlyList<long> GetConnectionIdsForRenumbering(long projectId)
    {
        return connection.Query<long>(
            """
            SELECT c.Id
            FROM Connection c
            JOIN DevicePin dp ON dp.Id = c.FromDevicePinId
            JOIN Device d ON d.Id = dp.DeviceId
            JOIN PlacementPin pp ON pp.DevicePinId = dp.Id
            JOIN Placement p ON p.Id = pp.PlacementId
            JOIN Page pg ON pg.Id = p.PageId
            WHERE d.ProjectId = @projectId
            ORDER BY pg.SortOrder, c.Id;
            """,
            new { projectId }).ToList();
    }

    public long InsertConnectionEnd(ConnectionEnd end)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO ConnectionEnd (ConnectionId, End, TerminationEnabled, TerminationType, TerminationPartId, StrippingLengthMm)
            VALUES (@ConnectionId, @End, @TerminationEnabled, @TerminationType, @TerminationPartId, @StrippingLengthMm)
            RETURNING Id;
            """,
            new
            {
                end.ConnectionId,
                End = (int)end.End,
                end.TerminationEnabled,
                TerminationType = (int)end.TerminationType,
                end.TerminationPartId,
                end.StrippingLengthMm,
            });
    }

    public IReadOnlyList<ConnectionEnd> GetConnectionEnds(long connectionId)
    {
        return connection.Query<ConnectionEndRow>(
            "SELECT * FROM ConnectionEnd WHERE ConnectionId = @connectionId;", new { connectionId })
            .Select(r => r.ToModel())
            .ToList();
    }

    // long/double rather than int/bool to match Dapper's exact-type-match constructor materialization
    // against SQLite's underlying INTEGER/REAL reader types (see PartRepository.PartRow for detail).
    private sealed record ConnectionEndRow(long Id, long ConnectionId, long End, long TerminationEnabled,
        long TerminationType, long? TerminationPartId, double? StrippingLengthMm)
    {
        public ConnectionEnd ToModel() => new()
        {
            Id = Id,
            ConnectionId = ConnectionId,
            End = (ConnectionEndDesignator)(int)End,
            TerminationEnabled = TerminationEnabled != 0,
            TerminationType = (TerminationType)(int)TerminationType,
            TerminationPartId = TerminationPartId,
            StrippingLengthMm = StrippingLengthMm,
        };
    }
}
