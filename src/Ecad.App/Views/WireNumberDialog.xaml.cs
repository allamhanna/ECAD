using System.Windows;

namespace Ecad.App.Views;

/// <summary>Minimal single-field dialog for renaming a Connection's wire number — same shape as
/// M5's original DeviceTagDialog before M6 upgraded it to full IEC 81346 segments; a wire number
/// has no comparable segment structure, so it stays this simple.</summary>
public partial class WireNumberDialog : Window
{
    private readonly Func<string, bool> _isAvailable;

    public WireNumberDialog(string currentWireNumber, Func<string, bool> isAvailable)
    {
        InitializeComponent();
        _isAvailable = isAvailable;
        NumberBox.Text = currentWireNumber;
        Loaded += (_, _) =>
        {
            NumberBox.Focus();
            NumberBox.SelectAll();
        };
    }

    public string WireNumber { get; private set; } = string.Empty;

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        var value = NumberBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            ShowValidation("Wire number is required.");
            return;
        }

        if (!_isAvailable(value))
        {
            ShowValidation($"Wire number '{value}' is already used in this project.");
            return;
        }

        WireNumber = value;
        DialogResult = true;
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }
}
