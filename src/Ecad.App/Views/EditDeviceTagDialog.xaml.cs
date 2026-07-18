using System.Windows;
using Ecad.App.ViewModels;

namespace Ecad.App.Views;

/// <summary>Edits an existing Device's Function/Location/Tag in place — the Devices Navigator's
/// "Edit Tag..." action, replacing the Grid Editor's old inline cell-edit for this same data (the
/// navigator's grid is read-only, see DevicesNavigatorView). Single-device-only, same scoping
/// precedent as EditPageDialog/RenameSelectedPage. No uniqueness check, matching
/// DevicesGridViewModel's already-documented accepted simplification (unlike DeviceTagDialog's
/// canvas new-placement flow).</summary>
public partial class EditDeviceTagDialog : Window
{
    public EditDeviceTagDialog(DeviceRow device)
    {
        InitializeComponent();
        FunctionBox.Text = device.FunctionSegment ?? string.Empty;
        LocationBox.Text = device.LocationSegment ?? string.Empty;
        TagBox.Text = device.DeviceTagSegment;

        Loaded += (_, _) =>
        {
            TagBox.Focus();
            TagBox.SelectAll();
        };
    }

    public string? FunctionSegment { get; private set; }
    public string? LocationSegment { get; private set; }
    public string TagSegment { get; private set; } = string.Empty;

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TagBox.Text))
        {
            ValidationText.Text = "Tag is required.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        FunctionSegment = string.IsNullOrWhiteSpace(FunctionBox.Text) ? null : FunctionBox.Text.Trim();
        LocationSegment = string.IsNullOrWhiteSpace(LocationBox.Text) ? null : LocationBox.Text.Trim();
        TagSegment = TagBox.Text.Trim();
        DialogResult = true;
    }
}
