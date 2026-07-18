using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ecad.App.ViewModels;
using Ecad.Core.Models;

namespace Ecad.App.Views;

/// <summary>The Cables Navigator — a trimmed, read-only sibling of the Devices Navigator (same
/// interaction model: double-click a row to jump to a page, right-click for actions), backed by the
/// same CablesGridViewModel the old (now-removed) Grid Editor "Cables" tab used. Core management
/// (a master-detail pane in the old Grid Editor tab, too wide for this sidebar) moved into
/// ManageCableCoresDialog instead of being trimmed away.</summary>
public partial class CablesNavigatorView : UserControl
{
    public CablesNavigatorView() => InitializeComponent();

    /// <summary>DataGrid.SelectedItems isn't two-way bindable — mirrored here the same way
    /// DevicesNavigatorView/the old CablesGridView did (ADR-014's OfType&lt;T&gt; defensive pattern).</summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not CablesGridViewModel vm) return;
        vm.SelectedCables.Clear();
        foreach (var cable in Grid.SelectedItems.OfType<Cable>()) vm.SelectedCables.Add(cable);
        vm.NotifyCableSelectionChanged();
    }

    private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not CablesGridViewModel vm || Grid.SelectedItem is not Cable cable) return;

        if (!vm.NavigateToCable(cable))
        {
            MessageBox.Show("This cable isn't wired to any page yet.", "No Page Found",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnEditCableClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CablesGridViewModel vm || vm.SelectedCables.Count != 1) return;
        var cable = vm.SelectedCables[0];

        var dialog = new EditCableDialog(cable) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;

        cable.Tag = dialog.TagValue;
        cable.TypeDesignation = dialog.TypeDesignationValue;
        cable.LengthMm = dialog.LengthMmValue;
        cable.EndTypeClassification = dialog.EndTypeClassificationValue;
        vm.CommitCableEdit(cable);
    }

    private void OnManageCoresClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CablesGridViewModel vm || vm.SelectedCables.Count != 1) return;
        var cable = vm.SelectedCables[0];
        vm.SelectedCable = cable; // drives Cores loading (OnSelectedCableChanged)

        new ManageCableCoresDialog(vm, cable) { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    /// <summary>WPF's DataGrid doesn't select a row on right-click by default — without this, the
    /// context menu's actions would silently act on whatever was selected *before* the right-click
    /// instead of the row actually under the cursor (same fix DevicesNavigatorView needed).</summary>
    private void OnPreviewRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { Item: Cable cable } &&
            !Grid.SelectedItems.Contains(cable))
        {
            Grid.SelectedItem = cable;
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
