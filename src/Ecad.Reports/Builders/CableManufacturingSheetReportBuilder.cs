using Ecad.Core.Enums;
using Ecad.Core.Models;
using Ecad.Reports.LayoutSchema;

namespace Ecad.Reports.Builders;

public sealed record CableManufacturingSheetResult(ReportLayout Layout, ReportDataContext Data, string? Warning);

/// <summary>
/// Builds one cable manufacturing sheet (REQUIREMENTS 6.4 #4, F09-style) — called once per Cable. Picks
/// the ReportLayout whose Variant matches the cable's EndTypeClassification (e.g. FER-FER, FER-CONN),
/// falling back to the Variant-less default layout with a warning (never a hard failure) if no exact
/// match is loaded.
/// </summary>
public static class CableManufacturingSheetReportBuilder
{
    public const string ReportKind = "CableManufacturingSheet";

    public static CableManufacturingSheetResult Build(
        Cable cable,
        IReadOnlyList<CableCore> cableCores,
        IReadOnlyList<Connection> connectionsForThisCable,
        IReadOnlyList<ConnectionEndWithContext> connectionEnds,
        IReadOnlyList<DevicePin> devicePins,
        IReadOnlyList<Device> devices,
        IReadOnlyList<ReportLayout> layouts)
    {
        var layout = layouts.FirstOrDefault(l => l.ReportKind == ReportKind && l.Variant == cable.EndTypeClassification);
        string? warning = null;
        if (layout is null)
        {
            layout = layouts.FirstOrDefault(l => l.ReportKind == ReportKind && l.Variant is null);
            if (layout is not null)
                warning = $"No manufacturing sheet layout for end-type '{cable.EndTypeClassification}' on cable '{cable.Tag}' — used the default layout.";
        }
        if (layout is null)
            throw new InvalidOperationException($"No '{ReportKind}' layout template is loaded (not even a Variant-less default).");

        var devicePinsById = devicePins.ToDictionary(p => p.Id);
        var devicesById = devices.ToDictionary(d => d.Id);
        var connectionsByCoreId = connectionsForThisCable
            .Where(c => c.CableCoreId is not null)
            .ToDictionary(c => c.CableCoreId!.Value);
        var endsByConnectionId = connectionEnds.GroupBy(e => e.ConnectionId).ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var core in cableCores.OrderBy(c => c.CoreNumber))
        {
            connectionsByCoreId.TryGetValue(core.Id, out var connection);

            var fromPin = string.Empty;
            var toPin = string.Empty;
            var fromTermination = string.Empty;
            var toTermination = string.Empty;
            double? fromStripping = null;
            double? toStripping = null;

            if (connection is not null)
            {
                fromPin = ConnectionListReportBuilder.ResolvePinLabel(connection.FromDevicePinId, devicePinsById, devicesById);
                toPin = ConnectionListReportBuilder.ResolvePinLabel(connection.ToDevicePinId, devicePinsById, devicesById);
                endsByConnectionId.TryGetValue(connection.Id, out var ends);
                var fromEnd = ends?.FirstOrDefault(e => e.End == ConnectionEndDesignator.From);
                var toEnd = ends?.FirstOrDefault(e => e.End == ConnectionEndDesignator.To);
                fromTermination = fromEnd?.TerminationEnabled == true ? fromEnd.TerminationType.ToString() : string.Empty;
                toTermination = toEnd?.TerminationEnabled == true ? toEnd.TerminationType.ToString() : string.Empty;
                fromStripping = fromEnd?.StrippingLengthMm;
                toStripping = toEnd?.StrippingLengthMm;
            }

            rows.Add(new Dictionary<string, object?>
            {
                ["CoreNumber"] = core.CoreNumber,
                ["Color"] = core.Color ?? string.Empty,
                ["FromPin"] = fromPin,
                ["ToPin"] = toPin,
                ["FromTermination"] = fromTermination,
                ["ToTermination"] = toTermination,
                ["FromStrippingMm"] = fromStripping,
                ["ToStrippingMm"] = toStripping,
            });
        }

        var context = new ReportDataContext();
        context.SetScalar("Cable.Tag", cable.Tag);
        context.SetScalar("Cable.TypeDesignation", cable.TypeDesignation ?? string.Empty);
        context.SetScalar("Cable.LengthMm", cable.LengthMm);
        context.SetScalar("Cable.EndTypeClassification", cable.EndTypeClassification ?? string.Empty);
        context.SetTable("Cores", rows);

        return new CableManufacturingSheetResult(layout, context, warning);
    }
}
