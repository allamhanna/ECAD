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
        DataContext = new MainViewModel();
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
