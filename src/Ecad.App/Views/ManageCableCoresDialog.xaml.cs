using System.Windows;
using System.Windows.Controls;
using Ecad.App.ViewModels;
using Ecad.Core.Models;

namespace Ecad.App.Views;

/// <summary>The Cables Navigator's "Manage Cores..." action — the master-detail Cores pane lifted
/// out of the old Grid Editor's CablesGridView (which had room for it side-by-side) into a dialog,
/// since a ~260px sidebar column doesn't. Bound to the same CablesGridViewModel instance the
/// navigator itself uses, so Cores/SelectedCore/AddCoreCommand/DeleteSelectedCoreCommand need no new
/// plumbing — this just displays whatever's already loaded for the cable passed in (which must
/// already be CablesGridViewModel.SelectedCable, since that's what drives Cores).</summary>
public partial class ManageCableCoresDialog : Window
{
    public ManageCableCoresDialog(CablesGridViewModel viewModel, Cable cable)
    {
        InitializeComponent();
        DataContext = viewModel;
        TitleText.Text = $"Cores for cable '{cable.Tag}'";
    }

    private void OnCoreRowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (DataContext is not CablesGridViewModel vm) return;
        if (e.Row.Item is not CableCore core) return;

        // RowEditEnding fires before the grid's own binding update finishes committing to the source
        // object — defer to let the binding land first (same pattern as every other editable grid).
        Dispatcher.BeginInvoke(() => vm.CommitCoreEdit(core));
    }
}
