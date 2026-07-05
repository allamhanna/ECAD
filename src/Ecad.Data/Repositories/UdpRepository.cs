using Dapper;
using Ecad.Core.Enums;
using Ecad.Core.Models;
using Microsoft.Data.Sqlite;

namespace Ecad.Data.Repositories;

public class UdpRepository(SqliteConnection connection)
{
    public long InsertDefinition(UdpDefinition definition)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO UdpDefinition (Name, DataType, Unit, EnumValuesJson, AppliesToEntityType)
            VALUES (@Name, @DataTypeValue, @Unit, @EnumValuesJson, @AppliesToEntityTypeValue)
            RETURNING Id;
            """,
            new
            {
                definition.Name,
                DataTypeValue = (int)definition.DataType,
                definition.Unit,
                definition.EnumValuesJson,
                AppliesToEntityTypeValue = (int)definition.AppliesToEntityType,
            });
    }

    public long InsertValue(UdpValue value)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO UdpValue (DefinitionId, EntityType, EntityId, Value)
            VALUES (@DefinitionId, @EntityTypeValue, @EntityId, @Value)
            RETURNING Id;
            """,
            new
            {
                value.DefinitionId,
                EntityTypeValue = (int)value.EntityType,
                value.EntityId,
                value.Value,
            });
    }

    public IReadOnlyList<UdpValue> GetValuesForEntity(UdpEntityType entityType, long entityId)
    {
        return connection.Query<UdpValueRow>(
            "SELECT * FROM UdpValue WHERE EntityType = @entityType AND EntityId = @entityId;",
            new { entityType = (int)entityType, entityId })
            .Select(r => r.ToModel())
            .ToList();
    }

    // long rather than int to match Dapper's exact-type-match constructor materialization against
    // SQLite's underlying INTEGER reader type (see PartRepository.PartRow for detail).
    private sealed record UdpValueRow(long Id, long DefinitionId, long EntityType, long EntityId, string? Value)
    {
        public UdpValue ToModel() => new()
        {
            Id = Id,
            DefinitionId = DefinitionId,
            EntityType = (UdpEntityType)(int)EntityType,
            EntityId = EntityId,
            Value = Value,
        };
    }
}
