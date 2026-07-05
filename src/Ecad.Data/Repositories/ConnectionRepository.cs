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
