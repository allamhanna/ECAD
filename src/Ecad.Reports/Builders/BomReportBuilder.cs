using Ecad.Core.Enums;
using Ecad.Core.Models;
using Ecad.Reports.LayoutSchema;

namespace Ecad.Reports.Builders;

public enum BomGroupingMode
{
    PerProject,
    PerLocation,
    PerCableAssembly,
}

/// <summary>One occurrence of a Part somewhere in the project — one per Device.PartId, one per enabled
/// ConnectionEnd.TerminationPartId, one per Cable.PartId. OwningCableId is set when this occurrence
/// belongs to a cable assembly (a cable's own part, or a termination on one of its wires) — those
/// instances are reported once, inside that cable's module, and are excluded from the flat top-level
/// totals (REQUIREMENTS 6.4/6.3: "no double counting").</summary>
internal sealed record PartUsageInstance(long PartId, long? OwningCableId, string? LocationSegment);

/// <summary>Builds the BOM/parts-list report (REQUIREMENTS 6.4 #2): aggregated by article number with
/// quantities, three grouping modes, cable assemblies reported as modules without double-counting their
/// terminations against the flat project/location totals.</summary>
public static class BomReportBuilder
{
    public static ReportDataContext Build(
        IReadOnlyList<Device> devices,
        IReadOnlyList<DevicePin> devicePins,
        IReadOnlyList<Connection> connections,
        IReadOnlyList<ConnectionEndWithContext> connectionEnds,
        IReadOnlyList<Cable> cables,
        IReadOnlyList<Part> parts,
        BomGroupingMode mode)
    {
        var devicesById = devices.ToDictionary(d => d.Id);
        var devicePinsById = devicePins.ToDictionary(p => p.Id);
        var connectionsById = connections.ToDictionary(c => c.Id);
        var partsById = parts.ToDictionary(p => p.Id);
        var cablesById = cables.ToDictionary(c => c.Id);

        var instances = new List<PartUsageInstance>();

        foreach (var device in devices)
        {
            if (device.PartId is { } partId)
                instances.Add(new PartUsageInstance(partId, OwningCableId: null, device.LocationSegment));
        }

        foreach (var cable in cables)
        {
            if (cable.PartId is { } partId)
                instances.Add(new PartUsageInstance(partId, OwningCableId: cable.Id, LocationSegment: null));
        }

        foreach (var end in connectionEnds)
        {
            if (!end.TerminationEnabled || end.TerminationPartId is not { } terminationPartId) continue;

            connectionsById.TryGetValue(end.ConnectionId, out var connection);
            var owningCableId = connection?.CableId;

            string? location = null;
            if (owningCableId is null)
            {
                var devicePinId = end.End == ConnectionEndDesignator.From ? end.FromDevicePinId : end.ToDevicePinId;
                if (devicePinsById.TryGetValue(devicePinId, out var pin) && devicesById.TryGetValue(pin.DeviceId, out var device))
                    location = device.LocationSegment;
            }

            instances.Add(new PartUsageInstance(terminationPartId, owningCableId, location));
        }

        var rows = mode switch
        {
            BomGroupingMode.PerCableAssembly => BuildPerCableAssemblyRows(instances, cablesById, partsById),
            BomGroupingMode.PerLocation => BuildFlatRows(instances.Where(i => i.OwningCableId is null), partsById, groupByLocation: true),
            _ => BuildFlatRows(instances.Where(i => i.OwningCableId is null), partsById, groupByLocation: false),
        };

        var context = new ReportDataContext();
        context.SetTable("BomLines", rows);
        return context;
    }

    private static List<IReadOnlyDictionary<string, object?>> BuildFlatRows(
        IEnumerable<PartUsageInstance> instances, Dictionary<long, Part> partsById, bool groupByLocation)
    {
        var groups = groupByLocation
            ? instances.GroupBy(i => (i.PartId, Location: i.LocationSegment ?? string.Empty))
            : instances.GroupBy(i => (i.PartId, Location: string.Empty));

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var group in groups.OrderBy(g => g.Key.PartId))
            rows.Add(BuildRow(group.Key.PartId, group.Count(), group.Key.Location, module: string.Empty, partsById));
        return rows;
    }

    /// <summary>Flat rows for every instance NOT owned by a cable, followed by one module block per
    /// cable — each cabled instance (the cable's own Part plus its wires' terminations) counted exactly
    /// once here, never again in the flat rows above.</summary>
    private static List<IReadOnlyDictionary<string, object?>> BuildPerCableAssemblyRows(
        List<PartUsageInstance> instances, Dictionary<long, Cable> cablesById, Dictionary<long, Part> partsById)
    {
        var rows = BuildFlatRows(instances.Where(i => i.OwningCableId is null), partsById, groupByLocation: false);

        foreach (var cableGroup in instances.Where(i => i.OwningCableId is not null)
                     .GroupBy(i => i.OwningCableId!.Value)
                     .OrderBy(g => g.Key))
        {
            cablesById.TryGetValue(cableGroup.Key, out var cable);
            var moduleTag = cable?.Tag ?? $"Cable {cableGroup.Key}";
            foreach (var partGroup in cableGroup.GroupBy(i => i.PartId).OrderBy(g => g.Key))
                rows.Add(BuildRow(partGroup.Key, partGroup.Count(), location: string.Empty, module: moduleTag, partsById));
        }

        return rows;
    }

    private static IReadOnlyDictionary<string, object?> BuildRow(long partId, int quantity, string location, string module, Dictionary<long, Part> partsById)
    {
        partsById.TryGetValue(partId, out var part);
        return new Dictionary<string, object?>
        {
            ["ExternalKey"] = part?.ExternalKey ?? string.Empty,
            ["TypeNumber"] = part?.TypeNumber ?? string.Empty,
            ["Description"] = part?.Description1 ?? string.Empty,
            ["Location"] = location,
            ["Quantity"] = quantity,
            ["Module"] = module,
        };
    }
}
