using System.Linq;
using System.Windows.Controls;
using Ecad.App.ViewModels;

namespace Ecad.App.Views;

public partial class DevicesGridView : UserControl
{
    public DevicesGridView()
    {
        InitializeComponent();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not DevicesGridViewModel vm) return;

        // OfType, not a plain foreach cast: DataGrid.SelectedItems can include the internal "new
        // row" placeholder object when a multi-select spans it, which isn't a DeviceRow and throws
        // an InvalidCastException on an implicit cast (a real crash a user hit on this exact pattern
        // in ConnectionsGridView — see ADR-014). CanUserAddRows="False" already removes the
        // placeholder row itself; this is the defensive second layer.
        vm.SelectedDevices.Clear();
        foreach (var device in Grid.SelectedItems.OfType<DeviceRow>()) vm.SelectedDevices.Add(device);
        vm.NotifySelectionChanged();
    }

    private void OnRowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (DataContext is not DevicesGridViewModel vm) return;
        if (e.Row.Item is not DeviceRow device) return;

        // RowEditEnding fires before the grid's own binding update finishes committing to the
        // source object, so persisting synchronously here can read stale values — defer to let the
        // binding land first.
        Dispatcher.BeginInvoke(() => vm.CommitDeviceEdit(device));
    }
}
