using System.Windows.Controls;

namespace Ecad.App.Views;

/// <summary>
/// Parts Library's tab content (M10: extracted from the old PartsLibraryWindow). Project-independent
/// and a singleton — MainViewModel.OpenPartsLibrary constructs its PartsLibraryViewModel and owns the
/// tab, so (unlike the old Window) this view no longer constructs its own DataContext.
/// </summary>
public partial class PartsLibraryView : UserControl
{
    public PartsLibraryView()
    {
        InitializeComponent();
    }
}
