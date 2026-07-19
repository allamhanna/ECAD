using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using Ecad.Core.Models;
using Microsoft.Data.Sqlite;

namespace Ecad.Data.Repositories;

public class DeviceRepository(SqliteConnection connection)
{
    public long InsertDevice(Device device, IDbTransaction? transaction = null)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO Device (ProjectId, FunctionSegment, LocationSegment, DeviceTagSegment, PartId)
            VALUES (@ProjectId, @FunctionSegment, @LocationSegment, @DeviceTagSegment, @PartId)
            RETURNING Id;
            """,
            device, transaction);
    }

    public Device? GetDevice(long id)
    {
        return connection.QuerySingleOrDefault<Device>("SELECT * FROM Device WHERE Id = @id;", new { id });
    }

    public long InsertDevicePin(DevicePin pin, IDbTransaction? transaction = null)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO DevicePin (DeviceId, Name, Function, TechnicalData)
            VALUES (@DeviceId, @Name, @Function, @TechnicalData)
            RETURNING Id;
            """,
            pin, transaction);
    }

    public IReadOnlyList<DevicePin> GetDevicePins(long deviceId)
    {
        return connection.Query<DevicePin>("SELECT * FROM DevicePin WHERE DeviceId = @deviceId;", new { deviceId }).ToList();
    }

    /// <summary>M12: every DevicePin in the project in one query — report Builders resolve an arbitrary
    /// Connection.From/ToDevicePinId back to its owning Device without one query per connection.</summary>
    public IReadOnlyList<DevicePin> GetAllDevicePins(long projectId)
    {
        return connection.Query<DevicePin>(
            """
            SELECT dp.* FROM DevicePin dp
            JOIN Device d ON d.Id = dp.DeviceId
            WHERE d.ProjectId = @projectId;
            """,
            new { projectId }).ToList();
    }

    /// <summary>Deletes the Device row; DevicePin/Placement/PlacementPin all cascade per the M1 schema.
    /// Callers (ProjectSession.DeletePlacement, M6) only call this once a Device has no Placements left.</summary>
    public void DeleteDevice(long deviceId, IDbTransaction? transaction = null)
    {
        connection.Execute("DELETE FROM Device WHERE Id = @deviceId;", new { deviceId }, transaction);
    }

    public void UpdateDeviceTag(long deviceId, string? function, string? location, string deviceTag)
    {
        connection.Execute(
            "UPDATE Device SET FunctionSegment = @function, LocationSegment = @location, DeviceTagSegment = @deviceTag WHERE Id = @deviceId;",
            new { deviceId, function, location, deviceTag });
    }

    /// <summary>M8: finally writes to Device.PartId, unused since M1 (Section 6.2's Devices grid).</summary>
    public void UpdateDevicePart(long deviceId, long? partId)
    {
        connection.Execute("UPDATE Device SET PartId = @partId WHERE Id = @deviceId;", new { deviceId, partId });
    }

    public void UpdateDevicePin(DevicePin pin)
    {
        connection.Execute(
            "UPDATE DevicePin SET Name = @Name, Function = @Function, TechnicalData = @TechnicalData WHERE Id = @Id;",
            pin);
    }

    /// <summary>Caller must ensure no Connection references this pin first — see ProjectSession.CanDeleteDevicePin.</summary>
    public void DeleteDevicePin(long devicePinId)
    {
        connection.Execute("DELETE FROM DevicePin WHERE Id = @devicePinId;", new { devicePinId });
    }

    public IReadOnlyList<Device> GetAllDevices(long projectId)
    {
        return connection.Query<Device>("SELECT * FROM Device WHERE ProjectId = @projectId ORDER BY Id;", new { projectId }).ToList();
    }

    /// <summary>The Devices Navigator's data source — every Device with at least one Placement whose
    /// symbol isn't InterruptionPoint (a Device that mixes an interruption-point placement with a real
    /// one is still "real" — it's no longer purely a navigation aid once it has an actual component).
    /// Exact complement of GetAllInterruptionPointDevices below. GetAllDevices itself stays untouched
    /// — it also backs the "attach to existing device" picker shown when placing any symbol, which
    /// must see every Device regardless of kind (that's how two interruption points get paired).</summary>
    public IReadOnlyList<Device> GetAllRealDevices(long projectId)
    {
        return connection.Query<Device>(
            """
            SELECT d.* FROM Device d
            WHERE d.ProjectId = @projectId
              AND EXISTS (
                SELECT 1 FROM Placement p
                JOIN Symbol s ON s.Id = p.SymbolId
                WHERE p.DeviceId = d.Id AND s.Name <> 'InterruptionPoint'
              )
            ORDER BY d.Id;
            """,
            new { projectId }).ToList();
    }

    /// <summary>The Interruption Points Navigator's data source — every Device whose Placements are
    /// exclusively the InterruptionPoint symbol. Exact complement of GetAllRealDevices above.</summary>
    public IReadOnlyList<Device> GetAllInterruptionPointDevices(long projectId)
    {
        return connection.Query<Device>(
            """
            SELECT d.* FROM Device d
            WHERE d.ProjectId = @projectId
              AND NOT EXISTS (
                SELECT 1 FROM Placement p
                JOIN Symbol s ON s.Id = p.SymbolId
                WHERE p.DeviceId = d.Id AND s.Name <> 'InterruptionPoint'
              )
            ORDER BY d.Id;
            """,
            new { projectId }).ToList();
    }

    /// <summary>Exact-match lookup for tag-uniqueness checks (Section 6.1: "tag uniqueness enforced per project"). Excludes a given Device (for rename-in-place).</summary>
    public Device? FindByTag(long projectId, string? function, string? location, string deviceTag, long? excludingDeviceId)
    {
        return connection.QuerySingleOrDefault<Device>(
            """
            SELECT * FROM Device
            WHERE ProjectId = @projectId
              AND DeviceTagSegment = @deviceTag
              AND FunctionSegment IS @function
              AND LocationSegment IS @location
              AND (@excludingDeviceId IS NULL OR Id != @excludingDeviceId)
            LIMIT 1;
            """,
            new { projectId, function, location, deviceTag, excludingDeviceId });
    }

    /// <summary>
    /// Simple sequential suggestion for a new Device's Designation within a Function+Location scope:
    /// the highest trailing integer among existing tags in that scope, plus one (or 1 if none). Not a
    /// configurable numbering scheme (see ADR-008) — just a starting point the user can freely edit.
    /// </summary>
    public string SuggestNextDesignation(long projectId, string? function, string? location)
    {
        var existingTags = connection.Query<string>(
            "SELECT DeviceTagSegment FROM Device WHERE ProjectId = @projectId AND FunctionSegment IS @function AND LocationSegment IS @location;",
            new { projectId, function, location });

        var maxNumber = 0;
        foreach (var tag in existingTags)
        {
            var match = Regex.Match(tag, @"(\d+)$");
            if (match.Success && int.Parse(match.Value) > maxNumber) maxNumber = int.Parse(match.Value);
        }
        return (maxNumber + 1).ToString();
    }
}
