using Dapper;
using Ecad.Core.Models;
using Microsoft.Data.Sqlite;

namespace Ecad.Data.Repositories;

public class CableRepository(SqliteConnection connection)
{
    public long InsertCable(Cable cable)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO Cable (ProjectId, Tag, PartId, TypeDesignation, LengthMm, EndTypeClassification)
            VALUES (@ProjectId, @Tag, @PartId, @TypeDesignation, @LengthMm, @EndTypeClassification)
            RETURNING Id;
            """,
            cable);
    }

    public Cable? GetCable(long id)
    {
        return connection.QuerySingleOrDefault<Cable>("SELECT * FROM Cable WHERE Id = @id;", new { id });
    }

    /// <summary>All cables belonging to this project (M8: Cables grid) — includes cables with zero
    /// Connections assigned yet (Section 5.6 allows a Cable to exist as pure data), which is why this
    /// filters on Cable.ProjectId directly rather than joining through Connection.</summary>
    public IReadOnlyList<Cable> GetAllCables(long projectId)
    {
        return connection.Query<Cable>("SELECT * FROM Cable WHERE ProjectId = @projectId ORDER BY Id;", new { projectId }).ToList();
    }

    public void UpdateCable(Cable cable)
    {
        connection.Execute(
            """
            UPDATE Cable SET Tag = @Tag, PartId = @PartId, TypeDesignation = @TypeDesignation,
                LengthMm = @LengthMm, EndTypeClassification = @EndTypeClassification
            WHERE Id = @Id;
            """,
            cable);
    }

    /// <summary>Caller must ensure no Connection references this cable first (Connection.CableId has
    /// no ON DELETE CASCADE) — see ProjectSession.CanDeleteCable.</summary>
    public void DeleteCable(long cableId)
    {
        connection.Execute("DELETE FROM Cable WHERE Id = @cableId;", new { cableId });
    }

    public long InsertCableCore(CableCore core)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO CableCore (CableId, CoreNumber, Color, CrossSectionMm2)
            VALUES (@CableId, @CoreNumber, @Color, @CrossSectionMm2)
            RETURNING Id;
            """,
            core);
    }

    public CableCore? GetCableCore(long id)
    {
        return connection.QuerySingleOrDefault<CableCore>("SELECT * FROM CableCore WHERE Id = @id;", new { id });
    }

    public IReadOnlyList<CableCore> GetCableCores(long cableId)
    {
        return connection.Query<CableCore>("SELECT * FROM CableCore WHERE CableId = @cableId;", new { cableId }).ToList();
    }

    public void UpdateCableCore(CableCore core)
    {
        connection.Execute(
            "UPDATE CableCore SET CoreNumber = @CoreNumber, Color = @Color, CrossSectionMm2 = @CrossSectionMm2 WHERE Id = @Id;",
            core);
    }

    public void DeleteCableCore(long cableCoreId)
    {
        connection.Execute("DELETE FROM CableCore WHERE Id = @cableCoreId;", new { cableCoreId });
    }
}
