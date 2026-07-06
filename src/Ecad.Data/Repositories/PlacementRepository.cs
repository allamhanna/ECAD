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
            SELECT p.Id AS PlacementId, p.DeviceId, d.DeviceTagSegment AS DeviceTag, p.PageId,
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
