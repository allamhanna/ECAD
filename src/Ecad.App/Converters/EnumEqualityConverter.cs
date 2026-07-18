using System.Globalization;
using System.Windows.Data;

namespace Ecad.App.Converters;

/// <summary>Backs a group of checkable MenuItems standing in for a radio choice over an enum (e.g.
/// the Page Navigator's grouping menu) — IsChecked is true exactly when the bound enum value equals
/// ConverterParameter. One-way only: the click itself flows through a Command, not this binding.</summary>
public sealed class EnumEqualityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && value.Equals(parameter);

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
