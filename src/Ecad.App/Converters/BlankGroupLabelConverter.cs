using System.Globalization;
using System.Windows.Data;

namespace Ecad.App.Converters;

/// <summary>A Page Navigator group header's Name is null whenever no page in that group has a value
/// for the chosen segment (Function/Location/DocumentType always normalize blank input to null, never
/// "" — see AddPageDialog) — shown as "(none)" instead of a blank-looking header.</summary>
public sealed class BlankGroupLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? "(none)" : value!;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
