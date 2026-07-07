using Dapper;
using Ecad.Core.Models;
using Microsoft.Data.Sqlite;

namespace Ecad.Data.Repositories;

public class PlacementRepository(SqliteConnection connection)
{
    public long InsertSymbol(Symbol symbol)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO Symbol (Name, LibraryName, SvgFilePath, Category)
            VALUES (@Name, @LibraryName, @SvgFilePath, @Category)
            RETURNING Id;
            """,
            symbol);
    }

    /// <summary>Looks up a Symbol by name in this database, inserting it if missing. The M1 Symbol table stays otherwise unused until a Placement is actually created (ADR-006).</summary>
    public long GetOrCreateSymbol(string name, string? libraryName, string? svgFilePath, string? category)
    {
        var existingId = connection.QuerySingleOrDefault<long?>("SELECT Id FROM Symbol WHERE Name = @name;", new { name });
        if (existingId is not null) return existingId.Value;

        return InsertSymbol(new Symbol { Name = name, LibraryName = libraryName, SvgFilePath = svgFilePath, Category = category });
    }

    public long InsertPlacement(Placement placement)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO Placement (DeviceId, PageId, SymbolId, X, Y, RotationDegrees, Mirrored, Variant)
            VALUES (@DeviceId, @PageId, @SymbolId, @X, @Y, @RotationDegrees, @Mirrored, @Variant)
            RETURNING Id;
            """,
            placement);
    }

    public Placement? GetPlacement(long id)
    {
        return connection.QuerySingleOrDefault<Placement>("SELECT * FROM Placement WHERE Id = @id;", new { id });
    }

    /// <summary>Deletes just this Placement row; PlacementPin rows cascade. Does not touch the Device
    /// or its DevicePins — callers decide separately whether those should be cleaned up too (M6).</summary>
    public void DeletePlacement(long placementId)
    {
        connection.Execute("DELETE FROM Placement WHERE Id = @placementId;", new { placementId });
    }

    public void UpdatePosition(long placementId, double x, double y)
    {
        connection.Execute("UPDATE Placement SET X = @x, Y = @y WHERE Id = @placementId;", new { placementId, x, y });
    }

    public void UpdateRotation(long placementId, int rotationDegrees, bool mirrored)
    {
        connection.Execute(
            "UPDATE Placement SET RotationDegrees = @rotationDegrees, Mirrored = @mirrored WHERE Id = @placementId;",
            new { placementId, rotationDegrees, mirrored });
    }

    /// <summary>All placements on a page, joined with their Device tag and Symbol info — everything the canvas needs to render the page.</summary>
    public IReadOnlyList<PlacementWithSymbol> GetPlacementsForPage(long pageId)
    {
        return connection.Query<PlacementWithSymbol>(
            """
            SELECT p.Id AS PlacementId, p.DeviceId, d.DeviceTagSegment AS DeviceTag,
                   d.FunctionSegment, d.LocationSegment, p.PageId,
                   p.X, p.Y, p.RotationDegrees, p.Mirrored,
                   s.Name AS SymbolName, s.SvgFilePath AS SymbolSvgFilePath
            FROM Placement p
            JOIN Device d ON d.Id = p.DeviceId
            JOIN Symbol s ON s.Id = p.SymbolId
            WHERE p.PageId = @pageId
            ORDER BY p.Id;
            """,
            new { pageId }).ToList();
    }

    /// <summary>The other placements of the same Device as the given placement, with their page's number for cross-reference display (Section 5.4).</summary>
    public IReadOnlyList<SiblingPlacementRef> GetSiblingPlacementRefs(long placementId)
    {
        // A settable-property row type, not the SiblingPlacementRef record directly: with zero
        // matching rows (the common case — most devices have only one placement), Dapper still has
        // to generate a deserializer from the reader's column schema alone, and Microsoft.Data.Sqlite
        // can't resolve a concrete CLR type for the computed PageLabel expression without an actual
        // row to inspect — it reports byte[], which breaks record constructor-based materialization
        // (see ADR-002). A plain class's lenient property-setter mapping tolerates that; PageLabel is
        // built from the raw PageNumberSegment here instead of via SQL COALESCE.
        var rows = connection.Query<SiblingPlacementRow>(
            """
            SELECT p2.Id AS PlacementId, p2.PageId, pg.PageNumberSegment
            FROM Placement p1
            JOIN Placement p2 ON p2.DeviceId = p1.DeviceId AND p2.Id != p1.Id
            JOIN Page pg ON pg.Id = p2.PageId
            WHERE p1.Id = @placementId
            ORDER BY p2.Id;
            """,
            new { placementId });

        return rows.Select(r => new SiblingPlacementRef(r.PlacementId, r.PageId, r.PageNumberSegment ?? $"#{r.PageId}")).ToList();
    }

    private sealed class SiblingPlacementRow
    {
        public long PlacementId { get; set; }
        public long PageId { get; set; }
        public string? PageNumberSegment { get; set; }
    }

    /// <summary>The DevicePin names this placement exposes — used to recreate an equivalent placement on undo-of-delete.</summary>
    public IReadOnlyList<string> GetPlacementPinNames(long placementId)
    {
        return connection.Query<string>(
            """
            SELECT dp.Name
            FROM PlacementPin pp
            JOIN DevicePin dp ON dp.Id = pp.DevicePinId
            WHERE pp.PlacementId = @placementId
            ORDER BY pp.Id;
            """,
            new { placementId }).ToList();
    }

    /// <summary>The DevicePins this placement exposes (id + name) — lets the canvas resolve each pin's world position via the symbol's matching-named connection point, without a further round-trip.</summary>
    public IReadOnlyList<PlacementPinInfo> GetPlacementPins(long placementId)
    {
        return connection.Query<PlacementPinInfo>(
            """
            SELECT dp.Id AS DevicePinId, dp.Name
            FROM PlacementPin pp
            JOIN DevicePin dp ON dp.Id = pp.DevicePinId
            WHERE pp.PlacementId = @placementId
            ORDER BY pp.Id;
            """,
            new { placementId }).ToList();
    }

    public int CountPlacementsForDevice(long deviceId)
    {
        return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Placement WHERE DeviceId = @deviceId;", new { deviceId });
    }

    /// <summary>
    /// Deletes the DevicePins this placement exposes that no other placement also exposes — the
    /// pins "exclusive" to this placement (e.g. a contact block's 13/14, not shared with the coil's
    /// A1/A2). Must run before the Placement itself is deleted (its own PlacementPin rows are what
    /// this query checks against). Pins still referenced by a sibling placement are left alone.
    /// </summary>
    public void DeleteExclusiveDevicePinsForPlacement(long placementId)
    {
        connection.Execute(
            """
            DELETE FROM DevicePin
            WHERE Id IN (
                SELECT DevicePinId FROM PlacementPin WHERE PlacementId = @placementId
            )
            AND Id NOT IN (
                SELECT DevicePinId FROM PlacementPin WHERE PlacementId != @placementId
            );
            """,
            new { placementId });
    }

    public long AddPlacementPin(long placementId, long devicePinId)
    {
        return connection.ExecuteScalar<long>(
            "INSERT INTO PlacementPin (PlacementId, DevicePinId) VALUES (@placementId, @devicePinId) RETURNING Id;",
            new { placementId, devicePinId });
    }

    /// <summary>
    /// The other Placements of the same Device as the given Placement — the cross-reference set
    /// (Section 5.4: a relay coil placement lists its contacts' locations and vice versa, because
    /// they're all placements of one logical Device, regardless of which pins each one exposes).
    /// </summary>
    public IReadOnlyList<long> GetSiblingPlacementIds(long placementId)
    {
        return connection.Query<long>(
            """
            SELECT p2.Id
            FROM Placement p1
            JOIN Placement p2 ON p2.DeviceId = p1.DeviceId AND p2.Id != p1.Id
            WHERE p1.Id = @placementId;
            """,
            new { placementId }).ToList();
    }
}
