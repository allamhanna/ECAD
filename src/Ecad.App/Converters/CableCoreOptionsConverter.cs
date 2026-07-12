using System.Globalization;
using System.Windows.Data;
using Ecad.Core.Models;

namespace Ecad.App.Converters;

/// <summary>
/// M8: the Connections grid's CableCoreId column must offer only the cores belonging to that row's
/// own CableId — a plain DataGridComboBoxColumn can't filter its item source per row, so the column
/// is a DataGridTemplateColumn whose ComboBox.ItemsSource is a MultiBinding through this converter
/// (row's CableId + the grid's CoresByCableId lookup).
/// </summary>
public sealed class CableCoreOptionsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is [long cableId, Dictionary<long, List<CableCore>> coresByCableId] &&
            coresByCableId.TryGetValue(cableId, out var cores))
        {
            return cores;
        }
        return Array.Empty<CableCore>();
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
