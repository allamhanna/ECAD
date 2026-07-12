using System.Linq;
using System.Windows.Controls;
using Ecad.App.ViewModels;
using Ecad.Core.Models;

namespace Ecad.App.Views;

public partial class ConnectionsGridView : UserControl
{
    public ConnectionsGridView()
    {
        InitializeComponent();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ConnectionsGridViewModel vm) return;

        // OfType, not a plain foreach cast: DataGrid.SelectedItems can include the internal "new
        // row" placeholder object when a multi-select spans it, which isn't a Connection and threw
        // an InvalidCastException here on exactly this pattern (a real crash a user hit — ADR-014).
        // CanUserAddRows="False" already removes the placeholder row itself; this is the defensive
        // second layer.
        vm.SelectedConnections.Clear();
        foreach (var connection in Grid.SelectedItems.OfType<Connection>()) vm.SelectedConnections.Add(connection);
        vm.NotifySelectionChanged();
    }

    private void OnRowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (DataContext is not ConnectionsGridViewModel vm) return;
        if (e.Row.Item is not Connection connection) return;

        Dispatcher.BeginInvoke(() => vm.CommitConnectionEdit(connection));
    }
}
