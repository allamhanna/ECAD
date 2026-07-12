using System.Globalization;
using System.Windows;

namespace Ecad.App.Views;

/// <summary>
/// Places, edits, or removes a wire's connection definition point — the diagonal tick a user
/// explicitly adds to a wire, carrying its number/cross-section/color (a wire shows none of these
/// until one is placed). Shown both when placing a brand-new definition point (fields blank/suggested,
/// Remove hidden) and when double-clicking an existing one to edit it (fields pre-filled, Remove shown).
/// </summary>
public partial class DefinitionPointDialog : Window
{
    private readonly Func<string, bool> _isWireNumberAvailable;

    public DefinitionPointDialog(string? currentWireNumber, string? currentColor, double? currentCrossSectionMm2,
        bool isExisting, Func<string, bool> isWireNumberAvailable)
    {
        InitializeComponent();
        _isWireNumberAvailable = isWireNumberAvailable;

        NumberBox.Text = currentWireNumber ?? string.Empty;
        CrossSectionBox.Text = currentCrossSectionMm2?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        ColorBox.Text = currentColor ?? string.Empty;
        RemoveButton.Visibility = isExisting ? Visibility.Visible : Visibility.Collapsed;

        Loaded += (_, _) =>
        {
            NumberBox.Focus();
            NumberBox.SelectAll();
        };
    }

    public string? WireNumber { get; private set; }
    public string? Color { get; private set; }
    public double? CrossSectionMm2 { get; private set; }

    /// <summary>True when the user clicked Remove — the caller should clear the definition point
    /// entirely rather than apply WireNumber/Color/CrossSectionMm2 (which stay null/default in that case).</summary>
    public bool Removed { get; private set; }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        var wireNumber = string.IsNullOrWhiteSpace(NumberBox.Text) ? null : NumberBox.Text.Trim();
        if (wireNumber is not null && !_isWireNumberAvailable(wireNumber))
        {
            ShowValidation($"Wire number '{wireNumber}' is already used in this project.");
            return;
        }

        var crossSectionText = CrossSectionBox.Text.Trim();
        double? crossSectionMm2 = null;
        if (crossSectionText.Length > 0)
        {
            if (!double.TryParse(crossSectionText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            {
                ShowValidation("Cross-section must be a positive number.");
                return;
            }
            crossSectionMm2 = parsed;
        }

        WireNumber = wireNumber;
        Color = string.IsNullOrWhiteSpace(ColorBox.Text) ? null : ColorBox.Text.Trim();
        CrossSectionMm2 = crossSectionMm2;
        DialogResult = true;
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        Removed = true;
        DialogResult = true;
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }
}
