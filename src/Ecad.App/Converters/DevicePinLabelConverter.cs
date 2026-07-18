using System.Globalization;
using System.Windows.Data;
using Ecad.App.ViewModels;

namespace Ecad.App.Converters;

/// <summary>Resolves a From/To DevicePinId into its "Tag — PinName" label for the Connections
/// Navigator's read-only grid, against the same AllDevicePins list the old inline From/To pin
/// pickers already used — no new flattened row type needed, just this lookup instead of a raw id.</summary>
public sealed class DevicePinLabelConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is [long pinId, IEnumerable<DevicePinOption> allDevicePins])
        {
            var match = allDevicePins.FirstOrDefault(p => p.PinId == pinId);
            if (match is not null) return match.Label;
        }
        return "?";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
