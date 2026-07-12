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
}
