using System.Windows;
using Ecad.Reports.Builders;

namespace Ecad.App.Views;

public partial class BomGroupingDialog : Window
{
    public BomGroupingDialog() => InitializeComponent();

    public BomGroupingMode SelectedMode { get; private set; } = BomGroupingMode.PerProject;

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        SelectedMode = LocationRadio.IsChecked == true ? BomGroupingMode.PerLocation
            : CableAssemblyRadio.IsChecked == true ? BomGroupingMode.PerCableAssembly
            : BomGroupingMode.PerProject;
        DialogResult = true;
    }
}
