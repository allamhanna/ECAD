using System.Linq;
using System.Windows.Controls;
using Ecad.App.ViewModels;
using Ecad.Core.Models;

namespace Ecad.App.Views;

public partial class CablesGridView : UserControl
{
    public CablesGridView()
    {
        InitializeComponent();
    }

    private void OnCableSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not CablesGridViewModel vm) return;

        // OfType, not a plain foreach cast — see ADR-014: DataGrid.SelectedItems can include the
        // internal "new row" placeholder object on a multi-select spanning it, which isn't a Cable.
        // CanUserAddRows="False" already removes the placeholder row itself; this is the defensive
        // second layer.
        vm.SelectedCables.Clear();
        foreach (var cable in CablesGrid.SelectedItems.OfType<Cable>()) vm.SelectedCables.Add(cable);
        vm.NotifyCableSelectionChanged();
    }

    private void OnCableRowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (DataContext is not CablesGridViewModel vm) return;
        if (e.Row.Item is not Cable cable) return;

        Dispatcher.BeginInvoke(() => vm.CommitCableEdit(cable));
    }

    private void OnCoreRowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (DataContext is not CablesGridViewModel vm) return;
        if (e.Row.Item is not CableCore core) return;

        Dispatcher.BeginInvoke(() => vm.CommitCoreEdit(core));
    }
}
