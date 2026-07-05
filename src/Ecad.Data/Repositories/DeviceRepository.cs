using Dapper;
using Ecad.Core.Models;
using Microsoft.Data.Sqlite;

namespace Ecad.Data.Repositories;

public class DeviceRepository(SqliteConnection connection)
{
    public long InsertDevice(Device device)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO Device (ProjectId, FunctionSegment, LocationSegment, DeviceTagSegment, PartId)
            VALUES (@ProjectId, @FunctionSegment, @LocationSegment, @DeviceTagSegment, @PartId)
            RETURNING Id;
            """,
            device);
    }

    public Device? GetDevice(long id)
    {
        return connection.QuerySingleOrDefault<Device>("SELECT * FROM Device WHERE Id = @id;", new { id });
    }

    public long InsertDevicePin(DevicePin pin)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO DevicePin (DeviceId, Name, Function, TechnicalData)
            VALUES (@DeviceId, @Name, @Function, @TechnicalData)
            RETURNING Id;
            """,
            pin);
    }

    public IReadOnlyList<DevicePin> GetDevicePins(long deviceId)
    {
        return connection.Query<DevicePin>("SELECT * FROM DevicePin WHERE DeviceId = @deviceId;", new { deviceId }).ToList();
    }
}
