using Dapper;
using Ecad.Core.Models;
using Microsoft.Data.Sqlite;

namespace Ecad.Data.Repositories;

public class DefinitionPointRepository(SqliteConnection connection)
{
    public long Insert(DefinitionPoint point)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO DefinitionPoint (PageId, X, Y, WireNumber, Color, CrossSectionMm2, ConnectionId)
            VALUES (@PageId, @X, @Y, @WireNumber, @Color, @CrossSectionMm2, @ConnectionId)
            RETURNING Id;
            """,
            point);
    }

    public DefinitionPoint? Get(long id) =>
        connection.QuerySingleOrDefault<DefinitionPoint>("SELECT * FROM DefinitionPoint WHERE Id = @id;", new { id });

    public IReadOnlyList<DefinitionPoint> GetForPage(long pageId) =>
        connection.Query<DefinitionPoint>("SELECT * FROM DefinitionPoint WHERE PageId = @pageId ORDER BY Id;", new { pageId }).ToList();

    public DefinitionPoint? GetByConnectionId(long connectionId) =>
        connection.QuerySingleOrDefault<DefinitionPoint>("SELECT * FROM DefinitionPoint WHERE ConnectionId = @connectionId;", new { connectionId });

    public void UpdatePosition(long id, double x, double y) =>
        connection.Execute("UPDATE DefinitionPoint SET X = @x, Y = @y WHERE Id = @id;", new { id, x, y });

    public void UpdateData(long id, string? wireNumber, string? color, double? crossSectionMm2) =>
        connection.Execute(
            "UPDATE DefinitionPoint SET WireNumber = @wireNumber, Color = @color, CrossSectionMm2 = @crossSectionMm2 WHERE Id = @id;",
            new { id, wireNumber, color, crossSectionMm2 });

    public void SetConnection(long id, long? connectionId) =>
        connection.Execute("UPDATE DefinitionPoint SET ConnectionId = @connectionId WHERE Id = @id;", new { id, connectionId });

    public void UpdateRotation(long id, int rotationDegrees) =>
        connection.Execute("UPDATE DefinitionPoint SET RotationDegrees = @rotationDegrees WHERE Id = @id;", new { id, rotationDegrees });

    public void Delete(long id) =>
        connection.Execute("DELETE FROM DefinitionPoint WHERE Id = @id;", new { id });

    /// <summary>Every DefinitionPoint with a WireNumber, project-wide, ordered by its own page's
    /// SortOrder then its own (Y, X) — the deterministic order "Renumber Wires" assigns fresh
    /// sequential numbers in, now derived directly from the point's own position rather than through
    /// a connection's route.</summary>
    public IReadOnlyList<long> GetIdsForRenumbering(long projectId) =>
        connection.Query<long>(
            """
            SELECT dp.Id
            FROM DefinitionPoint dp
            JOIN Page pg ON pg.Id = dp.PageId
            WHERE pg.ProjectId = @projectId AND dp.WireNumber IS NOT NULL
            ORDER BY pg.SortOrder, dp.Y, dp.X;
            """,
            new { projectId }).ToList();

    /// <summary>Every wire number currently assigned, project-wide — feeds SuggestNextWireNumber and
    /// uniqueness checks (moved off Connection now that numbers live here).</summary>
    public IReadOnlyList<string> GetAllWireNumbers(long projectId) =>
        connection.Query<string>(
            """
            SELECT dp.WireNumber
            FROM DefinitionPoint dp
            JOIN Page pg ON pg.Id = dp.PageId
            WHERE pg.ProjectId = @projectId AND dp.WireNumber IS NOT NULL;
            """,
            new { projectId }).ToList();

    /// <summary>Exact-match lookup for wire-number-uniqueness checks, excluding a given DefinitionPoint
    /// (for edit-in-place) — same pattern as ConnectionRepository's old FindByWireNumber.</summary>
    public DefinitionPoint? FindByWireNumber(long projectId, string wireNumber, long? excludingId) =>
        connection.QuerySingleOrDefault<DefinitionPoint>(
            """
            SELECT dp.* FROM DefinitionPoint dp
            JOIN Page pg ON pg.Id = dp.PageId
            WHERE pg.ProjectId = @projectId AND dp.WireNumber = @wireNumber
              AND (@excludingId IS NULL OR dp.Id != @excludingId)
            LIMIT 1;
            """,
            new { projectId, wireNumber, excludingId });

    /// <summary>Every ConnectionId currently attached to a DefinitionPoint, project-wide — the Grid
    /// Editor's Connections tab uses this to decide which rows' Color/Cross-section become read-only
    /// ("set via canvas", the same treatment WireNumber already gets).</summary>
    public IReadOnlyList<long> GetAttachedConnectionIds(long projectId) =>
        connection.Query<long>(
            """
            SELECT dp.ConnectionId
            FROM DefinitionPoint dp
            JOIN Page pg ON pg.Id = dp.PageId
            WHERE pg.ProjectId = @projectId AND dp.ConnectionId IS NOT NULL;
            """,
            new { projectId }).ToList();
}
