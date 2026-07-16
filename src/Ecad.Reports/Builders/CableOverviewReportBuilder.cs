using Ecad.Core.Models;
using Ecad.Reports.LayoutSchema;

namespace Ecad.Reports.Builders;

/// <summary>Builds the "Cables" table data for the cable overview report (REQUIREMENTS 6.4 #3): tag,
/// type, length, core count, from/to locations (derived from the locations of the devices at either end
/// of every wire currently assigned to the cable).</summary>
public static class CableOverviewReportBuilder
{
    public static ReportDataContext Build(
        IReadOnlyList<Cable> cables,
        IReadOnlyList<CableCore> cableCores,
        IReadOnlyList<Connection> connections,
        IReadOnlyList<DevicePin> devicePins,
        IReadOnlyList<Device> devices)
    {
        var devicePinsById = devicePins.ToDictionary(p => p.Id);
        var devicesById = devices.ToDictionary(d => d.Id);
        var coreCountByCableId = cableCores.GroupBy(c => c.CableId).ToDictionary(g => g.Key, g => g.Count());
        var connectionsByCableId = connections.Where(c => c.CableId is not null)
            .GroupBy(c => c.CableId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var cable in cables.OrderBy(c => c.Tag, StringComparer.OrdinalIgnoreCase))
        {
            connectionsByCableId.TryGetValue(cable.Id, out var cableConnections);
            var (from, to) = ResolveLocations(cableConnections ?? [], devicePinsById, devicesById);

            rows.Add(new Dictionary<string, object?>
            {
                ["Tag"] = cable.Tag,
                ["TypeDesignation"] = cable.TypeDesignation ?? string.Empty,
                ["LengthMm"] = cable.LengthMm,
                ["CoreCount"] = coreCountByCableId.GetValueOrDefault(cable.Id, 0),
                ["FromLocation"] = from,
                ["ToLocation"] = to,
            });
        }

        var context = new ReportDataContext();
        context.SetTable("Cables", rows);
        return context;
    }

    private static (string From, string To) ResolveLocations(
        List<Connection> connections, Dictionary<long, DevicePin> devicePinsById, Dictionary<long, Device> devicesById)
    {
        var fromLocations = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var toLocations = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in connections)
        {
            if (ResolveLocation(connection.FromDevicePinId, devicePinsById, devicesById) is { Length: > 0 } from) fromLocations.Add(from);
            if (ResolveLocation(connection.ToDevicePinId, devicePinsById, devicesById) is { Length: > 0 } to) toLocations.Add(to);
        }
        return (string.Join(", ", fromLocations), string.Join(", ", toLocations));
    }

    private static string ResolveLocation(long devicePinId, Dictionary<long, DevicePin> devicePinsById, Dictionary<long, Device> devicesById)
    {
        if (!devicePinsById.TryGetValue(devicePinId, out var pin)) return string.Empty;
        if (!devicesById.TryGetValue(pin.DeviceId, out var device)) return string.Empty;
        return device.LocationSegment ?? string.Empty;
    }
}
