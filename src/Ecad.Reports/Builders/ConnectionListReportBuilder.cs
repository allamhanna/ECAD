using Ecad.Core.Enums;
using Ecad.Core.Models;
using Ecad.Reports.LayoutSchema;

namespace Ecad.Reports.Builders;

/// <summary>Builds the "Connections" table data for the connection list report (REQUIREMENTS 6.4 #1):
/// from/to tag+pin, wire number, color, cross-section, per-end termination, cable/core if assigned.
/// A pure data-in/document-out transform — never touches SQL, per Ecad.Reports' dependency rule.</summary>
public static class ConnectionListReportBuilder
{
    public static ReportDataContext Build(
        IReadOnlyList<Connection> connections,
        IReadOnlyList<ConnectionEndWithContext> connectionEnds,
        IReadOnlyList<Device> devices,
        IReadOnlyList<DevicePin> devicePins,
        IReadOnlyList<Part> parts,
        IReadOnlyList<Cable> cables,
        IReadOnlyList<CableCore> cableCores)
    {
        var devicePinsById = devicePins.ToDictionary(p => p.Id);
        var devicesById = devices.ToDictionary(d => d.Id);
        var partsById = parts.ToDictionary(p => p.Id);
        var cablesById = cables.ToDictionary(c => c.Id);
        var coresById = cableCores.ToDictionary(c => c.Id);
        var endsByConnectionId = connectionEnds.GroupBy(e => e.ConnectionId).ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var connection in connections.OrderBy(c => c.WireNumber ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            endsByConnectionId.TryGetValue(connection.Id, out var ends);
            var fromEnd = ends?.FirstOrDefault(e => e.End == ConnectionEndDesignator.From);
            var toEnd = ends?.FirstOrDefault(e => e.End == ConnectionEndDesignator.To);

            var cable = connection.CableId is { } cableId && cablesById.TryGetValue(cableId, out var cb) ? cb : null;
            var core = connection.CableCoreId is { } coreId && coresById.TryGetValue(coreId, out var co) ? co : null;

            rows.Add(new Dictionary<string, object?>
            {
                ["FromTag"] = ResolvePinLabel(connection.FromDevicePinId, devicePinsById, devicesById),
                ["ToTag"] = ResolvePinLabel(connection.ToDevicePinId, devicePinsById, devicesById),
                ["WireNumber"] = connection.WireNumber ?? string.Empty,
                ["Color"] = connection.Color ?? string.Empty,
                ["CrossSectionMm2"] = connection.CrossSectionMm2,
                ["FromTermination"] = FormatTermination(fromEnd, partsById),
                ["ToTermination"] = FormatTermination(toEnd, partsById),
                ["CableTag"] = cable?.Tag ?? string.Empty,
                ["CoreNumber"] = core is null ? string.Empty : core.CoreNumber.ToString(),
            });
        }

        var context = new ReportDataContext();
        context.SetTable("Connections", rows);
        return context;
    }

    // Duplicated (not shared) across report Builders deliberately, matching this codebase's own
    // per-file-duplication convention (e.g. WireHitTester/CableLineHitTester's DistanceToSegment,
    // ConnectionsGridViewModel/TerminationsGridViewModel's FormatDeviceTag).
    internal static string ResolvePinLabel(long devicePinId, Dictionary<long, DevicePin> devicePinsById, Dictionary<long, Device> devicesById)
    {
        if (!devicePinsById.TryGetValue(devicePinId, out var pin)) return string.Empty;
        if (!devicesById.TryGetValue(pin.DeviceId, out var device)) return pin.Name;

        var prefix = string.Empty;
        if (!string.IsNullOrEmpty(device.FunctionSegment)) prefix += $"={device.FunctionSegment} ";
        if (!string.IsNullOrEmpty(device.LocationSegment)) prefix += $"+{device.LocationSegment} ";
        return $"{prefix}-{device.DeviceTagSegment}:{pin.Name}";
    }

    private static string FormatTermination(ConnectionEndWithContext? end, Dictionary<long, Part> partsById)
    {
        if (end is null || !end.TerminationEnabled) return string.Empty;
        if (end.TerminationPartId is { } partId && partsById.TryGetValue(partId, out var part))
            return part.TypeNumber ?? part.ExternalKey;
        return end.TerminationType.ToString();
    }
}
