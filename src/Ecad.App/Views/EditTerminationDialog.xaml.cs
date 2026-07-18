using System.Globalization;
using System.Windows;
using Ecad.App.ViewModels;
using Ecad.Core.Enums;

namespace Ecad.App.Views;

/// <summary>Edits a Termination's own three per-row fields — the Terminals Navigator's "Edit
/// Termination..." action, replacing the old Grid Editor grid's inline cell-edit now that the
/// navigator's grid is read-only (see TerminalsNavigatorView). TerminationPartId stays
/// bulk-assign-only (ADR-012), unchanged — not exposed here.</summary>
public partial class EditTerminationDialog : Window
{
    public EditTerminationDialog(TerminationsGridViewModel viewModel, TerminationRow row)
    {
        InitializeComponent();

        TypeCombo.ItemsSource = viewModel.TerminationTypeOptions;
        TerminatedCheck.IsChecked = row.TerminationEnabled;
        TypeCombo.SelectedItem = row.TerminationType;
        StrippingLengthBox.Text = row.StrippingLengthMm?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public bool TerminationEnabledValue { get; private set; }
    public TerminationType TerminationTypeValue { get; private set; }
    public double? StrippingLengthMmValue { get; private set; }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        double? strippingLength = null;
        if (!string.IsNullOrWhiteSpace(StrippingLengthBox.Text))
        {
            if (!double.TryParse(StrippingLengthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                ValidationText.Text = "Stripping length must be a number.";
                ValidationText.Visibility = Visibility.Visible;
                return;
            }
            strippingLength = parsed;
        }

        TerminationEnabledValue = TerminatedCheck.IsChecked == true;
        TerminationTypeValue = TypeCombo.SelectedItem is TerminationType selected ? selected : TerminationType.None;
        StrippingLengthMmValue = strippingLength;
        DialogResult = true;
    }
}
