using Dapper;
using Ecad.Core.Models;
using Microsoft.Data.Sqlite;

namespace Ecad.Data.Repositories;

public class CableLineRepository(SqliteConnection connection)
{
    public long InsertCableLine(CableLine line)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO CableLine (PageId, X1, Y1, X2, Y2, CableId)
            VALUES (@PageId, @X1, @Y1, @X2, @Y2, @CableId)
            RETURNING Id;
            """,
            line);
    }

    public CableLine? GetCableLine(long id) =>
        connection.QuerySingleOrDefault<CableLine>("SELECT * FROM CableLine WHERE Id = @id;", new { id });

    public IReadOnlyList<CableLine> GetCableLinesForPage(long pageId) =>
        connection.Query<CableLine>("SELECT * FROM CableLine WHERE PageId = @pageId ORDER BY Id;", new { pageId }).ToList();

    public void UpdateGeometry(long id, double x1, double y1, double x2, double y2) =>
        connection.Execute("UPDATE CableLine SET X1 = @x1, Y1 = @y1, X2 = @x2, Y2 = @y2 WHERE Id = @id;", new { id, x1, y1, x2, y2 });

    public void UpdateCableId(long id, long cableId) =>
        connection.Execute("UPDATE CableLine SET CableId = @cableId WHERE Id = @id;", new { id, cableId });

    public void DeleteCableLine(long id) =>
        connection.Execute("DELETE FROM CableLine WHERE Id = @id;", new { id });

    public long InsertCrossing(CableLineCrossing crossing)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO CableLineCrossing (CableLineId, ConnectionId, CableCoreId)
            VALUES (@CableLineId, @ConnectionId, @CableCoreId)
            RETURNING Id;
            """,
            crossing);
    }

    public IReadOnlyList<CableLineCrossing> GetCrossingsForLine(long cableLineId) =>
        connection.Query<CableLineCrossing>("SELECT * FROM CableLineCrossing WHERE CableLineId = @cableLineId ORDER BY Id;", new { cableLineId }).ToList();

    public CableLineCrossing? GetCrossing(long id) =>
        connection.QuerySingleOrDefault<CableLineCrossing>("SELECT * FROM CableLineCrossing WHERE Id = @id;", new { id });

    public void UpdateCrossingRotation(long id, int rotationDegrees) =>
        connection.Execute("UPDATE CableLineCrossing SET RotationDegrees = @rotationDegrees WHERE Id = @id;", new { id, rotationDegrees });

    public void DeleteCrossing(long id) =>
        connection.Execute("DELETE FROM CableLineCrossing WHERE Id = @id;", new { id });

    public void DeleteCrossingsForLine(long cableLineId) =>
        connection.Execute("DELETE FROM CableLineCrossing WHERE CableLineId = @cableLineId;", new { cableLineId });

    /// <summary>Every ConnectionId currently crossed (live, not orphaned) by any CableLine in this
    /// project — the Grid Editor's Connections tab uses this to make Cable/CableCore read-only for
    /// those rows ("set via canvas"), same treatment already applied for an attached DefinitionPoint.</summary>
    public IReadOnlyList<long> GetAttachedConnectionIds(long projectId) =>
        connection.Query<long>(
            """
            SELECT clc.ConnectionId
            FROM CableLineCrossing clc
            JOIN CableLine cl ON cl.Id = clc.CableLineId
            JOIN Page pg ON pg.Id = cl.PageId
            WHERE pg.ProjectId = @projectId AND clc.ConnectionId IS NOT NULL;
            """,
            new { projectId }).ToList();
}
