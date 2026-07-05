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
