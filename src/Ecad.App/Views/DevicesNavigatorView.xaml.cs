using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ecad.App.ViewModels;

namespace Ecad.App.Views;

/// <summary>The Devices Navigator — a trimmed, read-only sibling of the sidebar's Pages list (same
/// interaction model: double-click a row to jump to its page, right-click for actions), backed by
/// the same DevicesGridViewModel the old (now-removed) Grid Editor "Devices" tab used. Read-only by
/// design so double-click can only ever mean "jump" — no WPF cell-edit-mode ambiguity to worry
/// about; editing goes through the "Edit Tag..." dialog instead.</summary>
public partial class DevicesNavigatorView : UserControl
{
    public DevicesNavigatorView() => InitializeComponent();

    /// <summary>DataGrid.SelectedItems isn't two-way bindable — mirrored here the same way the old
    /// DevicesGridView did (ADR-014's OfType&lt;T&gt; defensive pattern).</summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not DevicesGridViewModel vm) return;
        vm.SelectedDevices.Clear();
        foreach (var device in Grid.SelectedItems.OfType<DeviceRow>()) vm.SelectedDevices.Add(device);
        vm.NotifySelectionChanged();
    }

    private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is DevicesGridViewModel vm && Grid.SelectedItem is DeviceRow device)
            vm.NavigateToDevice(device);
    }

    /// <summary>WPF's DataGrid doesn't select a row on right-click by default — without this, the
    /// context menu's "Edit Tag..."/"Delete Selected" would silently act on whatever was selected
    /// *before* the right-click instead of the row actually under the cursor.</summary>
    private void OnPreviewRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { Item: DeviceRow device } &&
            !Grid.SelectedItems.Contains(device))
        {
            Grid.SelectedItem = device;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnEditTagClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicesGridViewModel vm || vm.SelectedDevices.Count != 1) return;
        var device = vm.SelectedDevices[0];

        var dialog = new EditDeviceTagDialog(device) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;

        device.FunctionSegment = dialog.FunctionSegment;
        device.LocationSegment = dialog.LocationSegment;
        device.DeviceTagSegment = dialog.TagSegment;
        vm.CommitDeviceEdit(device);
    }
}
