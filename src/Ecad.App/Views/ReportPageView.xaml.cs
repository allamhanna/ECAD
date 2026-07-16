using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Ecad.App.ViewModels;

namespace Ecad.App.Views;

/// <summary>
/// A report's table columns are data-driven (each report kind has its own column set, sourced from its
/// JSON layout template) — DataGrid can't declare that in XAML, so columns are (re)built here whenever
/// the ViewModel's Columns collection changes (construction, or after Regenerate/re-binding).
/// </summary>
public partial class ReportPageView : UserControl
{
    public ReportPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ReportPageViewModel oldViewModel)
            oldViewModel.Columns.CollectionChanged -= OnColumnsChanged;

        if (e.NewValue is ReportPageViewModel newViewModel)
        {
            newViewModel.Columns.CollectionChanged += OnColumnsChanged;
            RebuildColumns(newViewModel.Columns);
        }
    }

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is ReportPageViewModel viewModel) RebuildColumns(viewModel.Columns);
    }

    private void RebuildColumns(IEnumerable<ReportColumn> columns)
    {
        ReportGrid.Columns.Clear();
        foreach (var column in columns)
        {
            ReportGrid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Header,
                Binding = new Binding($"[{column.DataFieldKey}]") { Mode = BindingMode.OneWay },
            });
        }
    }
}
