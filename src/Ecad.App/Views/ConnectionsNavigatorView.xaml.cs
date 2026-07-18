using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ecad.App.ViewModels;
using Ecad.Core.Models;

namespace Ecad.App.Views;

/// <summary>The Connections Navigator — a trimmed, read-only sibling of the Devices/Cables
/// navigators (same interaction model: double-click a row to jump to a page, right-click for
/// actions), backed by the same ConnectionsGridViewModel the old (now-removed) Grid Editor
/// "Connections" tab used. No Delete Selected here — a Connection has no independent identity to
/// delete (ADR-011/015), only Edit Connection... and the bulk-set header carried over unchanged.</summary>
public partial class ConnectionsNavigatorView : UserControl
{
    public ConnectionsNavigatorView() => InitializeComponent();

    /// <summary>DataGrid.SelectedItems isn't two-way bindable — mirrored here the same way every
    /// other navigator does (ADR-014's OfType&lt;T&gt; defensive pattern).</summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ConnectionsGridViewModel vm) return;
        vm.SelectedConnections.Clear();
        foreach (var connection in Grid.SelectedItems.OfType<Connection>()) vm.SelectedConnections.Add(connection);
        vm.NotifySelectionChanged();
    }

    private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ConnectionsGridViewModel vm && Grid.SelectedItem is Connection connection)
            vm.NavigateToConnection(connection);
    }

    private void OnEditConnectionClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConnectionsGridViewModel vm || vm.SelectedConnections.Count != 1) return;
        var connection = vm.SelectedConnections[0];

        var dialog = new EditConnectionDialog(vm, connection) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;

        connection.FromDevicePinId = dialog.FromDevicePinId;
        connection.ToDevicePinId = dialog.ToDevicePinId;
        connection.Color = dialog.ColorValue;
        connection.CrossSectionMm2 = dialog.CrossSectionMm2Value;
        connection.LengthMm = dialog.LengthMmValue;
        connection.CableId = dialog.CableIdValue;
        connection.CableCoreId = dialog.CableCoreIdValue;
        vm.CommitConnectionEdit(connection);
    }

    /// <summary>WPF's DataGrid doesn't select a row on right-click by default — without this, the
    /// context menu's "Edit Connection..." would silently act on whatever was selected *before* the
    /// right-click instead of the row actually under the cursor (same fix every prior navigator needed).</summary>
    private void OnPreviewRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { Item: Connection connection } &&
            !Grid.SelectedItems.Contains(connection))
        {
            Grid.SelectedItem = connection;
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
}
