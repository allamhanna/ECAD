using System.Windows.Controls;

namespace Ecad.App.Views;

/// <summary>
/// The Grid Editor's tab content (M10: extracted from the old per-project GridEditorWindow into a
/// UserControl hosted by MainWindow's document TabControl). Unlike SchematicPageView, this needs
/// none of the DataContextChanged/InvalidateVisual workaround — every child here is an ordinary
/// WPF-bound DataGrid, not a manually-painted SKElement, so WPF's own binding system repaints it.
/// </summary>
public partial class GridEditorView : UserControl
{
    public GridEditorView()
    {
        InitializeComponent();
    }
}
