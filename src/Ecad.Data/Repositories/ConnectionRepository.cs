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

    public void UpdateConnectionColor(long connectionId, string? color)
    {
        connection.Execute("UPDATE Connection SET Color = @color WHERE Id = @connectionId;", new { connectionId, color });
    }

    public void UpdateConnectionCrossSection(long connectionId, double? crossSectionMm2)
    {
        connection.Execute("UPDATE Connection SET CrossSectionMm2 = @crossSectionMm2 WHERE Id = @connectionId;", new { connectionId, crossSectionMm2 });
    }

    /// <summary>A genuine pre-existing gap found while building the Connections Navigator's Edit
    /// dialog: LengthMm was only ever written at InsertConnection time — the old Grid Editor's
    /// "Length (mm)" column was bound and editable, but no UPDATE path existed anywhere, so an edit
    /// silently reverted on the next Refresh(). Fixed here rather than carried forward.</summary>
    public void UpdateConnectionLength(long connectionId, double? lengthMm)
    {
        connection.Execute("UPDATE Connection SET LengthMm = @lengthMm WHERE Id = @connectionId;", new { connectionId, lengthMm });
    }

    public void UpdateConnectionCable(long connectionId, long? cableId, long? cableCoreId)
    {
        connection.Execute("UPDATE Connection SET CableId = @cableId, CableCoreId = @cableCoreId WHERE Id = @connectionId;", new { connectionId, cableId, cableCoreId });
    }

    public void UpdateConnectionEndpoints(long connectionId, long fromDevicePinId, long toDevicePinId)
    {
        connection.Execute(
            "UPDATE Connection SET FromDevicePinId = @fromDevicePinId, ToDevicePinId = @toDevicePinId WHERE Id = @connectionId;",
            new { connectionId, fromDevicePinId, toDevicePinId });
    }

    /// <summary>M8: guards Cable deletion (ProjectSession.CanDeleteCable) — a Cable still referenced
    /// by a Connection must not be deleted silently.</summary>
    public bool AnyConnectionReferencesCable(long cableId)
    {
        var count = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM Connection WHERE CableId = @cableId;", new { cableId });
        return count > 0;
    }

    /// <summary>The Cables Navigator's "jump to page" target — a Cable has no PageId of its own and
    /// can validly have zero Connections at all (pure data, REQUIREMENTS 5.6), so this returns null in
    /// that case rather than throwing. When a Connection does exist, both its ends resolve to the same
    /// page (ADR-009: no cross-page wiring), so only the From end needs joining. Ordered by
    /// Page.SortOrder/Page.Id/Connection.Id — "first" means earliest page in project order, same
    /// convention as GetFirstPlacementForDevice. Same lenient-row-mapping reason as that method (zero-row
    /// case still needs a concrete type for the computed PageLabel column).</summary>
    public SiblingPlacementRef? GetFirstConnectionPageForCable(long cableId)
    {
        var row = connection.QuerySingleOrDefault<ConnectionPageRow>(
            """
            SELECT pp.PlacementId AS PlacementId, p.PageId, pg.PageNumberSegment
            FROM Connection c
            JOIN PlacementPin pp ON pp.DevicePinId = c.FromDevicePinId
            JOIN Placement p ON p.Id = pp.PlacementId
            JOIN Page pg ON pg.Id = p.PageId
            WHERE c.CableId = @cableId
            ORDER BY pg.SortOrder, pg.Id, c.Id
            LIMIT 1;
            """,
            new { cableId });

        return row is null ? null : new SiblingPlacementRef(row.PlacementId, row.PageId, row.PageNumberSegment ?? $"#{row.PageId}");
    }

    private sealed class ConnectionPageRow
    {
        public long PlacementId { get; set; }
        public long PageId { get; set; }
        public string? PageNumberSegment { get; set; }
    }

    /// <summary>The Connections Navigator's "jump to page" target — unlike a Cable, a Connection is
    /// only ever created between two already-placed pins, so it always resolves to exactly one page
    /// (both ends resolve to the same page, ADR-009 — no "first among several" ordering needed, this
    /// is keyed directly on Connection.Id rather than a Cable's possibly-many Connections).</summary>
    public SiblingPlacementRef? GetConnectionPage(long connectionId)
    {
        var row = connection.QuerySingleOrDefault<ConnectionPageRow>(
            """
            SELECT pp.PlacementId AS PlacementId, p.PageId, pg.PageNumberSegment
            FROM Connection c
            JOIN PlacementPin pp ON pp.DevicePinId = c.FromDevicePinId
            JOIN Placement p ON p.Id = pp.PlacementId
            JOIN Page pg ON pg.Id = p.PageId
            WHERE c.Id = @connectionId;
            """,
            new { connectionId });

        return row is null ? null : new SiblingPlacementRef(row.PlacementId, row.PageId, row.PageNumberSegment ?? $"#{row.PageId}");
    }

    /// <summary>M8: run before deleting a CableCore, so any Connection assigned to that core is
    /// un-assigned (CableCoreId only) rather than blocking the core's deletion or orphaning the FK.</summary>
    public void ClearCableCoreReferences(long cableCoreId)
    {
        connection.Execute("UPDATE Connection SET CableCoreId = NULL WHERE CableCoreId = @cableCoreId;", new { cableCoreId });
    }

    /// <summary>Every Connection in the project, regardless of page (M8: the Connections grid isn't
    /// limited to same-page wiring the way the canvas's GetConnectionsForPage is).</summary>
    public IReadOnlyList<Connection> GetAllConnectionsForProject(long projectId)
    {
        return connection.Query<Connection>(
            """
            SELECT c.*
            FROM Connection c
            JOIN DevicePin dp ON dp.Id = c.FromDevicePinId
            JOIN Device d ON d.Id = dp.DeviceId
            WHERE d.ProjectId = @projectId
            ORDER BY c.Id;
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

    /// <summary>M9: every ConnectionEnd in the project, joined with its parent Connection's
    /// WireNumber/CrossSectionMm2/endpoint DevicePinIds — the Terminations tab's one read query
    /// (Section 6.3's filterable view). Endpoint labels are resolved by the caller, not here — see
    /// ConnectionEndWithContext's own doc comment.</summary>
    public IReadOnlyList<ConnectionEndWithContext> GetAllConnectionEndsWithContext(long projectId)
    {
        return connection.Query<ConnectionEndWithContextRow>(
            """
            SELECT ce.Id, ce.ConnectionId, ce.End, ce.TerminationEnabled, ce.TerminationType,
                   ce.TerminationPartId, ce.StrippingLengthMm,
                   c.WireNumber, c.CrossSectionMm2, c.FromDevicePinId, c.ToDevicePinId
            FROM ConnectionEnd ce
            JOIN Connection c ON c.Id = ce.ConnectionId
            JOIN DevicePin dp ON dp.Id = c.FromDevicePinId
            JOIN Device d ON d.Id = dp.DeviceId
            WHERE d.ProjectId = @projectId
            ORDER BY c.Id, ce.End;
            """,
            new { projectId })
            .Select(r => r.ToModel())
            .ToList();
    }

    public void UpdateConnectionEndTermination(long connectionEndId, bool terminationEnabled,
        TerminationType terminationType, long? terminationPartId, double? strippingLengthMm)
    {
        connection.Execute(
            """
            UPDATE ConnectionEnd
            SET TerminationEnabled = @terminationEnabled, TerminationType = @terminationTypeValue,
                TerminationPartId = @terminationPartId, StrippingLengthMm = @strippingLengthMm
            WHERE Id = @connectionEndId;
            """,
            new { connectionEndId, terminationEnabled, terminationTypeValue = (int)terminationType, terminationPartId, strippingLengthMm });
    }

    /// <summary>The narrow single-field setter the bulk-assign path uses — same "one setter per
    /// grid-editable column" precedent as UpdateConnectionColor/UpdateConnectionCrossSection.</summary>
    public void UpdateConnectionEndPart(long connectionEndId, long? terminationPartId)
    {
        connection.Execute("UPDATE ConnectionEnd SET TerminationPartId = @terminationPartId WHERE Id = @connectionEndId;",
            new { connectionEndId, terminationPartId });
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

    private sealed record ConnectionEndWithContextRow(long Id, long ConnectionId, long End, long TerminationEnabled,
        long TerminationType, long? TerminationPartId, double? StrippingLengthMm,
        string? WireNumber, double? CrossSectionMm2, long FromDevicePinId, long ToDevicePinId)
    {
        public ConnectionEndWithContext ToModel() => new()
        {
            Id = Id,
            ConnectionId = ConnectionId,
            End = (ConnectionEndDesignator)(int)End,
            TerminationEnabled = TerminationEnabled != 0,
            TerminationType = (TerminationType)(int)TerminationType,
            TerminationPartId = TerminationPartId,
            StrippingLengthMm = StrippingLengthMm,
            WireNumber = WireNumber,
            CrossSectionMm2 = CrossSectionMm2,
            FromDevicePinId = FromDevicePinId,
            ToDevicePinId = ToDevicePinId,
        };
    }
}
