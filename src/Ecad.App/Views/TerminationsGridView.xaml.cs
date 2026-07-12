using System.Linq;
using System.Windows.Controls;
using Ecad.App.ViewModels;

namespace Ecad.App.Views;

public partial class TerminationsGridView : UserControl
{
    public TerminationsGridView()
    {
        InitializeComponent();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not TerminationsGridViewModel vm) return;

        // OfType, not a plain foreach cast — see ADR-014: DataGrid.SelectedItems can include the
        // internal "new row" placeholder object on a multi-select spanning it.
        vm.SelectedRows.Clear();
        foreach (var row in Grid.SelectedItems.OfType<TerminationRow>()) vm.SelectedRows.Add(row);
        vm.NotifySelectionChanged();
    }

    private void OnRowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (DataContext is not TerminationsGridViewModel vm) return;
        if (e.Row.Item is not TerminationRow row) return;

        Dispatcher.BeginInvoke(() => vm.CommitTerminationEdit(row));
    }
}
