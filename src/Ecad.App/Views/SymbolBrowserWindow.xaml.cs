using System.Windows;
using Ecad.App.ViewModels;

namespace Ecad.App.Views;

public partial class SymbolBrowserWindow : Window
{
    public SymbolBrowserWindow()
    {
        InitializeComponent();
        DataContext = new SymbolBrowserViewModel();
    }
}
