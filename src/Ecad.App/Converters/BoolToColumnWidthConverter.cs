using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Ecad.App.Converters;

/// <summary>Collapses the Pages sidebar's ColumnDefinition to width 0 when hidden (View > Page
/// Navigator) — ColumnDefinition.Width binds directly, so no Style/DataTrigger indirection is needed.</summary>
public sealed class BoolToColumnWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? new GridLength(260) : new GridLength(0);

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
