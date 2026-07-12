using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ecad.Core.Enums;
using Ecad.Core.Models;
using Ecad.Data;
using Ecad.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace Ecad.App.ViewModels;

/// <summary>A ConnectionEnd's grid-display shape — its own termination fields plus enough context
/// from the parent Connection (wire number, cross-section, both endpoints' labels) to be readable
/// and filterable without extra lookups per row. TerminationPartExternalKey is read-only, same
/// reason as DeviceRow.PartExternalKey (ADR-012): the Library Part.Id used for picking and the
/// project-local Part.Id actually stored are different numbers.</summary>
public sealed class TerminationRow
{
    public long ConnectionEndId { get; set; }
    public long ConnectionId { get; set; }
    public ConnectionEndDesignator End { get; set; }
    public string? WireNumber { get; set; }
    public string EndPointLabel { get; set; } = string.Empty;
    public string OtherEndPointLabel { get; set; } = string.Empty;
    public double? CrossSectionMm2 { get; set; }
    public bool TerminationEnabled { get; set; }
    public TerminationType TerminationType { get; set; }
    public long? TerminationPartId { get; set; }
    public string? TerminationPartExternalKey { get; set; }
    public double? StrippingLengthMm { get; set; }
}

/// <summary>
/// M9 Terminations grid (Section 6.3): per-end termination toggle/type/stripping-length (inline
/// editable) and a filterable bulk-assign view for the termination Part — exactly the "all 0.5mm²
/// ends with termination=ferrule and no part assigned → bulk-assign part" example from REQUIREMENTS.
/// ConnectionEnd rows are never independently created or deleted by anything in this codebase
/// (always exactly two per Connection, made together by CreateConnection, cascade-deleted with it —
/// same spirit as ADR-015's decision that Connections themselves have no grid-delete), so this tab
/// only edits existing rows. Auto-populating StrippingLengthMm from part data is explicitly deferred
/// — no Part-family table anywhere carries a stripping-length value to populate it from.
/// </summary>
public sealed partial class TerminationsGridViewModel : ObservableObject, IDisposable
{
    private readonly ProjectSession _session;
    private readonly SqliteConnection _libraryConnection;
    private readonly PartRepository _libraryParts;

    public ObservableCollection<TerminationRow> AllRows { get; } = [];
    public ObservableCollection<TerminationRow> FilteredRows { get; } = [];
    public ObservableCollection<TerminationRow> SelectedRows { get; } = [];
    public ObservableCollection<Part> AllParts { get; } = [];

    /// <summary>TerminationType? options for the filter combo — null is the leading "(Any)" sentinel.</summary>
    public ObservableCollection<TerminationType?> TerminationTypeFilterOptions { get; } =
        new([null, ..Enum.GetValues<TerminationType>().Cast<TerminationType?>()]);

    /// <summary>Plain (non-nullable) TerminationType options for the grid's inline Type column.</summary>
    public ObservableCollection<TerminationType> TerminationTypeOptions { get; } = new(Enum.GetValues<TerminationType>());

    [ObservableProperty]
    private double? _filterCrossSectionMm2;

    [ObservableProperty]
    private TerminationType? _filterTerminationType;

    [ObservableProperty]
    private bool _filterUnassignedOnly;

    [ObservableProperty]
    private Part? _bulkAssignPart;

    public TerminationsGridViewModel(ProjectSession session)
    {
        _session = session;
        _libraryConnection = LibraryDatabase.Open();
        _libraryParts = new PartRepository(_libraryConnection);
        foreach (var part in _libraryParts.GetAllParts()) AllParts.Add(part);
        Refresh();
    }

    public void Refresh()
    {
        var devicePinLabels = BuildDevicePinLabels();

        AllRows.Clear();
        foreach (var end in _session.GetAllConnectionEndsWithContext())
        {
            var isFromEnd = end.End == ConnectionEndDesignator.From;
            var endPointLabel = devicePinLabels.GetValueOrDefault(isFromEnd ? end.FromDevicePinId : end.ToDevicePinId, "?");
            var otherEndPointLabel = devicePinLabels.GetValueOrDefault(isFromEnd ? end.ToDevicePinId : end.FromDevicePinId, "?");

            AllRows.Add(new TerminationRow
            {
                ConnectionEndId = end.Id,
                ConnectionId = end.ConnectionId,
                End = end.End,
                WireNumber = end.WireNumber,
                EndPointLabel = endPointLabel,
                OtherEndPointLabel = otherEndPointLabel,
                CrossSectionMm2 = end.CrossSectionMm2,
                TerminationEnabled = end.TerminationEnabled,
                TerminationType = end.TerminationType,
                TerminationPartId = end.TerminationPartId,
                TerminationPartExternalKey = end.TerminationPartId is { } partId ? _session.GetCachedPart(partId)?.ExternalKey : null,
                StrippingLengthMm = end.StrippingLengthMm,
            });
        }

        ApplyFilters();
    }

    private Dictionary<long, string> BuildDevicePinLabels()
    {
        var labels = new Dictionary<long, string>();
        foreach (var device in _session.GetAllDevices())
        {
            var tag = FormatDeviceTag(device);
            foreach (var pin in _session.GetDevicePins(device.Id))
                labels[pin.Id] = $"{tag} — {pin.Name}";
        }
        return labels;
    }

    private static string FormatDeviceTag(Device device)
    {
        var prefix = string.Empty;
        if (!string.IsNullOrEmpty(device.FunctionSegment)) prefix += $"={device.FunctionSegment} ";
        if (!string.IsNullOrEmpty(device.LocationSegment)) prefix += $"+{device.LocationSegment} ";
        return $"{prefix}-{device.DeviceTagSegment}";
    }

    partial void OnFilterCrossSectionMm2Changed(double? value) => ApplyFilters();
    partial void OnFilterTerminationTypeChanged(TerminationType? value) => ApplyFilters();
    partial void OnFilterUnassignedOnlyChanged(bool value) => ApplyFilters();

    private void ApplyFilters()
    {
        FilteredRows.Clear();
        foreach (var row in AllRows)
        {
            if (FilterCrossSectionMm2 is { } crossSection && row.CrossSectionMm2 != crossSection) continue;
            if (FilterTerminationType is { } type && row.TerminationType != type) continue;
            if (FilterUnassignedOnly && row.TerminationPartId is not null) continue;
            FilteredRows.Add(row);
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        FilterCrossSectionMm2 = null;
        FilterTerminationType = null;
        FilterUnassignedOnly = false;
    }

    /// <summary>Resolves the selected Library Part into this project's own cached copy first
    /// (ADR-012's EnsurePartCached, reused as-is) before bulk-assigning it to every selected end.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ApplyBulkAssignPart()
    {
        long? projectLocalPartId = BulkAssignPart is null ? null : _session.EnsurePartCached(BulkAssignPart.Id);
        _session.BulkUpdateConnectionEndPart(SelectedRows.Select(r => r.ConnectionEndId).ToList(), projectLocalPartId);
    }

    private bool HasSelection() => SelectedRows.Count > 0;

    /// <summary>Called by the view's SelectionChanged handler — DataGrid.SelectedItems isn't
    /// two-way bindable, so the code-behind syncs SelectedRows and then calls this to requery the
    /// selection-dependent command (same convention every other M8/M9 grid uses).</summary>
    public void NotifySelectionChanged() => ApplyBulkAssignPartCommand.NotifyCanExecuteChanged();

    /// <summary>Called by the view after a DataGrid row edit commits — TerminationEnabled/Type/
    /// StrippingLengthMm are inline-editable; TerminationPartId is bulk-assign-only (not touched here,
    /// same split as Device.PartId since ADR-012).</summary>
    public void CommitTerminationEdit(TerminationRow row) =>
        _session.UpdateConnectionEndTermination(row.ConnectionEndId, row.TerminationEnabled, row.TerminationType, row.TerminationPartId, row.StrippingLengthMm);

    public void Dispose() => _libraryConnection.Dispose();
}
