using System.Windows;

namespace Ecad.App.Views;

/// <summary>Minimal single-field dialog for a symbol placement's device tag. Full tag-segment
/// editing and auto-tag-from-page-context is M6 — this just takes one free-text tag.</summary>
public partial class DeviceTagDialog : Window
{
    public DeviceTagDialog(string? initialTag = null)
    {
        InitializeComponent();
        TagBox.Text = initialTag ?? string.Empty;
        Loaded += (_, _) =>
        {
            TagBox.Focus();
            TagBox.SelectAll();
        };
    }

    public string DeviceTag { get; private set; } = string.Empty;

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TagBox.Text))
        {
            ValidationText.Text = "Device Tag is required.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        DeviceTag = TagBox.Text.Trim();
        DialogResult = true;
    }
}
