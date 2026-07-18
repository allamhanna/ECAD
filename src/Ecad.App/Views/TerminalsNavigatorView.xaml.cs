using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ecad.App.ViewModels;

namespace Ecad.App.Views;

/// <summary>The Terminals Navigator — the last of the five, following the same read-only/
/// double-click-to-jump/right-click-to-edit shape as every prior navigator, backed by the same
/// TerminationsGridViewModel the old (now-removed) Grid Editor "Terminations" tab used. The filters
/// and bulk-assign-Part header carry over unchanged — they're the actual point of this tab (M9's
/// "filterable bulk-assign view"), not overflow to trim away.</summary>
public partial class TerminalsNavigatorView : UserControl
{
    public TerminalsNavigatorView() => InitializeComponent();

    /// <summary>DataGrid.SelectedItems isn't two-way bindable — mirrored here the same way every
    /// other navigator does (ADR-014's OfType&lt;T&gt; defensive pattern).</summary>
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not TerminationsGridViewModel vm) return;
        vm.SelectedRows.Clear();
        foreach (var row in Grid.SelectedItems.OfType<TerminationRow>()) vm.SelectedRows.Add(row);
        vm.NotifySelectionChanged();
    }

    private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is TerminationsGridViewModel vm && Grid.SelectedItem is TerminationRow row)
            vm.NavigateToTermination(row);
    }

    private void OnEditTerminationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TerminationsGridViewModel vm || vm.SelectedRows.Count != 1) return;
        var row = vm.SelectedRows[0];

        var dialog = new EditTerminationDialog(vm, row) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;

        row.TerminationEnabled = dialog.TerminationEnabledValue;
        row.TerminationType = dialog.TerminationTypeValue;
        row.StrippingLengthMm = dialog.StrippingLengthMmValue;
        vm.CommitTerminationEdit(row);
    }

    /// <summary>WPF's DataGrid doesn't select a row on right-click by default — without this, the
    /// context menu's "Edit Termination..." would silently act on whatever was selected *before* the
    /// right-click instead of the row actually under the cursor (same fix every prior navigator needed).</summary>
    private void OnPreviewRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { Item: TerminationRow row } &&
            !Grid.SelectedItems.Contains(row))
        {
            Grid.SelectedItem = row;
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
