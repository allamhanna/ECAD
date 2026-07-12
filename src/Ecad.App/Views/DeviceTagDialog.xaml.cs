using System.Windows;
using System.Windows.Controls;
using Ecad.Core.Models;
using Ecad.Core.ValueObjects;

namespace Ecad.App.Views;

/// <summary>
/// IEC 81346 segment-aware tag editor (M6) for the new-placement tag prompt: a device picker above
/// the segment fields — pick "New Device" (default) to create one with the given segments, or pick an
/// existing device to attach the new placement to it instead (segment fields disabled, tag inherited).
/// The rename-an-existing-placement mode this dialog used to also cover (double-click) moved to
/// SchematicPageView's docked device panel in M10 Phase 4 (ADR-017) — non-modal, so the canvas stays
/// interactive while it's open.
/// </summary>
public partial class DeviceTagDialog : Window
{
    private readonly IReadOnlyList<Device> _existingDevices;
    private readonly Func<string?, string?, string, long?, bool> _isTagAvailable;

    public DeviceTagDialog(IReadOnlyList<Device> existingDevices, string? suggestedFunction, string? suggestedLocation,
        string suggestedDesignation, Func<string?, string?, string, long?, bool> isTagAvailable)
    {
        InitializeComponent();
        _existingDevices = existingDevices;
        _isTagAvailable = isTagAvailable;

        DeviceCombo.Items.Add("— New Device —");
        foreach (var device in existingDevices) DeviceCombo.Items.Add(FormatTag(device));
        DeviceCombo.SelectedIndex = 0;
        DeviceCombo.SelectionChanged += (_, _) => UpdateSegmentFieldsEnabled();

        FunctionBox.Text = suggestedFunction ?? string.Empty;
        LocationBox.Text = suggestedLocation ?? string.Empty;
        DesignationBox.Text = suggestedDesignation;

        WireLivePreview();
        UpdatePreview();
        Loaded += (_, _) =>
        {
            DesignationBox.Focus();
            DesignationBox.SelectAll();
        };
    }

    /// <summary>Non-null when the user picked an existing device to attach to — the segment properties below don't apply.</summary>
    public long? SelectedExistingDeviceId { get; private set; }

    public string? Function { get; private set; }
    public string? Location { get; private set; }
    public string Designation { get; private set; } = string.Empty;

    private static string FormatTag(Device device) =>
        new DeviceTag(device.FunctionSegment, device.LocationSegment, device.DeviceTagSegment).ToString();

    private void WireLivePreview()
    {
        FunctionBox.TextChanged += (_, _) => UpdatePreview();
        LocationBox.TextChanged += (_, _) => UpdatePreview();
        DesignationBox.TextChanged += (_, _) => UpdatePreview();
    }

    private void UpdatePreview()
    {
        var tag = new DeviceTag(NullIfBlank(FunctionBox.Text), NullIfBlank(LocationBox.Text), NullIfBlank(DesignationBox.Text) ?? string.Empty);
        PreviewText.Text = tag.ToString();
    }

    private void UpdateSegmentFieldsEnabled()
    {
        var isNewDevice = DeviceCombo.SelectedIndex == 0;
        FunctionBox.IsEnabled = isNewDevice;
        LocationBox.IsEnabled = isNewDevice;
        DesignationBox.IsEnabled = isNewDevice;
    }

    private static string? NullIfBlank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (DeviceCombo.SelectedIndex > 0)
        {
            SelectedExistingDeviceId = _existingDevices[DeviceCombo.SelectedIndex - 1].Id;
            DialogResult = true;
            return;
        }

        var designation = NullIfBlank(DesignationBox.Text);
        if (designation is null)
        {
            ShowValidation("Designation is required.");
            return;
        }

        var function = NullIfBlank(FunctionBox.Text);
        var location = NullIfBlank(LocationBox.Text);

        if (!_isTagAvailable(function, location, designation, null))
        {
            ShowValidation($"Tag '{new DeviceTag(function, location, designation)}' is already used in this project.");
            return;
        }

        Function = function;
        Location = location;
        Designation = designation;
        DialogResult = true;
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }
}
