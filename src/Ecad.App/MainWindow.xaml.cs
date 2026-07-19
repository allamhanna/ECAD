using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ecad.App.ViewModels;
using Page = Ecad.Core.Models.Page;

namespace Ecad.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        DataContext = viewModel;

        // Deferred to Loaded (not run inside the constructor) so Application.Current.MainWindow is
        // already set by the time an auto-reopened page's dialogs try to own themselves against it.
        Loaded += (_, _) => viewModel.TryAutoReopenLastProject();
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }

    private void OnPagesDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && ((ListView)sender).SelectedItem is Page page)
        {
            viewModel.OpenPage(page);
        }
    }

    /// <summary>ListView.SelectedItems isn't bindable directly (unlike DataGrid's own selection, which
    /// this app already reads via code-behind per ADR-014) — mirrored here the same way.</summary>
    private void OnPagesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.UpdateSelectedPages(((ListView)sender).SelectedItems.OfType<Page>().ToList());
    }

    /// <summary>The Pages panel's "⚙" settings button opens its own ContextMenu on left-click rather
    /// than the usual right-click, since it IS the click target (there's nothing else on this button
    /// a right-click would make sense for).</summary>
    private void OnPagesSettingsClick(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.ContextMenu!.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    /// <summary>Delete key as an alternative to the ListView's own right-click "Delete Selected" —
    /// same command, same confirmation prompt, just reachable without a mouse.</summary>
    private void OnPagesKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || e.Key != Key.Delete) return;
        if (viewModel.DeleteSelectedPagesCommand.CanExecute(null)) viewModel.DeleteSelectedPagesCommand.Execute(null);
    }

    /// <summary>Ctrl+W (close the active tab) — handled here rather than a declarative
    /// Window.InputBindings KeyBinding, since CloseTabCommand needs a DocumentTabViewModel parameter
    /// and a CommandParameter binding on a freestanding InputBinding (not part of the visual tree) is
    /// the one scenario that's genuinely unreliable across WPF versions.</summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.W || Keyboard.Modifiers != ModifierKeys.Control) return;
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedTab is not { } tab) return;
        if (viewModel.CloseTabCommand.CanExecute(tab)) viewModel.CloseTabCommand.Execute(tab);
    }
}
