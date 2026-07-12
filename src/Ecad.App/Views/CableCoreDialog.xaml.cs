using System.Globalization;
using System.Windows;

namespace Ecad.App.Views;

/// <summary>
/// Edits a single cable line crossing's own core — number, cross-section, color — reachable by
/// double-clicking its tick on the canvas, same "edit an existing definition point" pattern as
/// DefinitionPointDialog, just sourced from a CableCore instead of a Connection.
/// </summary>
public partial class CableCoreDialog : Window
{
    private readonly int _currentCoreNumber;
    private readonly Func<int, bool> _isCoreNumberAvailable;

    public CableCoreDialog(int currentCoreNumber, string? currentColor, double? currentCrossSectionMm2, Func<int, bool> isCoreNumberAvailable)
    {
        InitializeComponent();
        _currentCoreNumber = currentCoreNumber;
        _isCoreNumberAvailable = isCoreNumberAvailable;

        CoreNumberBox.Text = currentCoreNumber.ToString(CultureInfo.InvariantCulture);
        CrossSectionBox.Text = currentCrossSectionMm2?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        ColorBox.Text = currentColor ?? string.Empty;

        Loaded += (_, _) =>
        {
            CoreNumberBox.Focus();
            CoreNumberBox.SelectAll();
        };
    }

    public int CoreNumber { get; private set; }
    public string? Color { get; private set; }
    public double? CrossSectionMm2 { get; private set; }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CoreNumberBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var coreNumber) || coreNumber <= 0)
        {
            ShowValidation("Core number must be a positive whole number.");
            return;
        }

        if (coreNumber != _currentCoreNumber && !_isCoreNumberAvailable(coreNumber))
        {
            ShowValidation($"Core {coreNumber} is already used on this cable.");
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

        CoreNumber = coreNumber;
        Color = string.IsNullOrWhiteSpace(ColorBox.Text) ? null : ColorBox.Text.Trim();
        CrossSectionMm2 = crossSectionMm2;
        DialogResult = true;
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }
}
