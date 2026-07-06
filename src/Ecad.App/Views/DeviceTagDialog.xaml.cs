using System.Windows;
using System.Windows.Controls;
using Ecad.Core.Models;
using Ecad.Core.ValueObjects;

namespace Ecad.App.Views;

/// <summary>
/// IEC 81346 segment-aware tag editor (M6). Two modes:
/// - Placement mode (ctor taking existingDevices): a device picker above the segment fields —
///   pick "New Device" (default) to create one with the given segments, or pick an existing device
///   to attach the new placement to it instead (segment fields disabled, tag inherited).
/// - Rename mode (ctor taking a Device): no picker, just edits that device's own segments.
/// </summary>
public partial class DeviceTagDialog : Window
{
    private readonly IReadOnlyList<Device> _existingDevices;
    private readonly Func<string?, string?, string, long?, bool> _isTagAvailable;
    private readonly long? _excludingDeviceId;

    public DeviceTagDialog(IReadOnlyList<Device> existingDevices, string? suggestedFunction, string? suggestedLocation,
        string suggestedDesignation, Func<string?, string?, string, long?, bool> isTagAvailable)
    {
        InitializeComponent();
        _existingDevices = existingDevices;
        _isTagAvailable = isTagAvailable;
        _excludingDeviceId = null;

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

    public DeviceTagDialog(Device device, Func<string?, string?, string, long?, bool> isTagAvailable)
    {
        InitializeComponent();
        _existingDevices = [];
        _isTagAvailable = isTagAvailable;
        _excludingDeviceId = device.Id;

        DeviceLabel.Visibility = Visibility.Collapsed;
        DeviceCombo.Visibility = Visibility.Collapsed;

        FunctionBox.Text = device.FunctionSegment ?? string.Empty;
        LocationBox.Text = device.LocationSegment ?? string.Empty;
        DesignationBox.Text = device.DeviceTagSegment;

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
        if (DeviceCombo.Visibility == Visibility.Visible && DeviceCombo.SelectedIndex > 0)
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

        if (!_isTagAvailable(function, location, designation, _excludingDeviceId))
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
