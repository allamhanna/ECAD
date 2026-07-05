using System.Windows;
using Ecad.App.ViewModels;

namespace Ecad.App.Views;

public partial class PartsLibraryWindow : Window
{
    public PartsLibraryWindow()
    {
        InitializeComponent();
        DataContext = new PartsLibraryViewModel();
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
