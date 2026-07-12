using System.Windows.Controls;

namespace Ecad.App.Views;

/// <summary>Symbol Browser's tab content (M10: extracted from the old SymbolBrowserWindow) — a
/// read-only wrapped thumbnail grid, project-independent and a singleton like PartsLibraryView.</summary>
public partial class SymbolBrowserView : UserControl
{
    public SymbolBrowserView()
    {
        InitializeComponent();
    }
}
