using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Ecad.App.ViewModels;
using Ecad.Core.Models;

namespace Ecad.App.Views;

/// <summary>Edits an existing Connection's rewireable fields in place — the Connections Navigator's
/// "Edit Connection..." action, replacing the old Grid Editor grid's inline comboboxes now that the
/// navigator's own grid is read-only (see ConnectionsNavigatorView). Color/Cross-section and
/// Cable/Core each disable per the same "set via canvas" guards ConnectionsGridViewModel's
/// CommitConnectionEdit already enforces (ConnectionIdsWithDefinitionPoint/
/// ConnectionIdsWithCableLineCrossing) — checked here before enabling the field, rather than
/// silently reverting an edit afterward.</summary>
public partial class EditConnectionDialog : Window
{
    private readonly ConnectionsGridViewModel _viewModel;

    public EditConnectionDialog(ConnectionsGridViewModel viewModel, Connection connection)
    {
        InitializeComponent();
        _viewModel = viewModel;

        FromPinCombo.ItemsSource = viewModel.AllDevicePins;
        FromPinCombo.SelectedValue = connection.FromDevicePinId;
        ToPinCombo.ItemsSource = viewModel.AllDevicePins;
        ToPinCombo.SelectedValue = connection.ToDevicePinId;

        ColorBox.Text = connection.Color ?? string.Empty;
        CrossSectionBox.Text = connection.CrossSectionMm2?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        LengthBox.Text = connection.LengthMm?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        CableCombo.ItemsSource = viewModel.AllCables;
        CableCombo.SelectedValue = connection.CableId;
        UpdateCoreOptions(connection.CableId);
        CableCoreCombo.SelectedValue = connection.CableCoreId;

        if (viewModel.ConnectionIdsWithDefinitionPoint.Contains(connection.Id))
        {
            ColorBox.IsEnabled = false;
            CrossSectionBox.IsEnabled = false;
        }
        if (viewModel.ConnectionIdsWithCableLineCrossing.Contains(connection.Id))
        {
            CableCombo.IsEnabled = false;
            CableCoreCombo.IsEnabled = false;
        }
    }

    private void OnCableSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCoreOptions(CableCombo.SelectedValue as long?);
        CableCoreCombo.SelectedValue = null;
    }

    private void UpdateCoreOptions(long? cableId)
    {
        CableCoreCombo.ItemsSource = cableId is { } id && _viewModel.CoresByCableId.TryGetValue(id, out var cores)
            ? cores
            : Array.Empty<CableCore>();
    }

    public long FromDevicePinId { get; private set; }
    public long ToDevicePinId { get; private set; }
    public string? ColorValue { get; private set; }
    public double? CrossSectionMm2Value { get; private set; }
    public double? LengthMmValue { get; private set; }
    public long? CableIdValue { get; private set; }
    public long? CableCoreIdValue { get; private set; }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (FromPinCombo.SelectedValue is not long fromPinId || ToPinCombo.SelectedValue is not long toPinId)
        {
            ValidationText.Text = "From Pin and To Pin are required.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        double? crossSection = null;
        if (!string.IsNullOrWhiteSpace(CrossSectionBox.Text))
        {
            if (!double.TryParse(CrossSectionBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedCrossSection))
            {
                ValidationText.Text = "Cross-section must be a number.";
                ValidationText.Visibility = Visibility.Visible;
                return;
            }
            crossSection = parsedCrossSection;
        }

        double? length = null;
        if (!string.IsNullOrWhiteSpace(LengthBox.Text))
        {
            if (!double.TryParse(LengthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedLength))
            {
                ValidationText.Text = "Length must be a number.";
                ValidationText.Visibility = Visibility.Visible;
                return;
            }
            length = parsedLength;
        }

        FromDevicePinId = fromPinId;
        ToDevicePinId = toPinId;
        ColorValue = string.IsNullOrWhiteSpace(ColorBox.Text) ? null : ColorBox.Text.Trim();
        CrossSectionMm2Value = crossSection;
        LengthMmValue = length;
        CableIdValue = CableCombo.SelectedValue as long?;
        CableCoreIdValue = CableCoreCombo.SelectedValue as long?;
        DialogResult = true;
    }
}
