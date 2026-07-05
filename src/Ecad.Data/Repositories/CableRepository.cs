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
            INSERT INTO Cable (Tag, PartId, TypeDesignation, LengthMm, EndTypeClassification)
            VALUES (@Tag, @PartId, @TypeDesignation, @LengthMm, @EndTypeClassification)
            RETURNING Id;
            """,
            cable);
    }

    public Cable? GetCable(long id)
    {
        return connection.QuerySingleOrDefault<Cable>("SELECT * FROM Cable WHERE Id = @id;", new { id });
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

    public IReadOnlyList<CableCore> GetCableCores(long cableId)
    {
        return connection.Query<CableCore>("SELECT * FROM CableCore WHERE CableId = @cableId;", new { cableId }).ToList();
    }
}
