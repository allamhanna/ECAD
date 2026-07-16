using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ecad.App.Reports;
using Ecad.Core.Models;
using Ecad.Data;
using Ecad.Reports;
using Ecad.Reports.Builders;
using Ecad.Reports.LayoutSchema;

namespace Ecad.App.ViewModels;

public sealed record ReportHeaderField(string Label, string Value);

public sealed record ReportColumn(string Header, string DataFieldKey, string Align);

/// <summary>
/// A generated report page's tab content — a plain in-app data table auto-populated from the current
/// drawing/project data, not a PDF (no QuestPDF or any other rendering engine is involved; PDF/Excel/CSV
/// export is a deliberately deferred, separately-licensed concern for a later milestone). On construction
/// and on every Regenerate, looks up the page's GeneratedReport identity, re-runs the matching report
/// Builder against the CURRENT session data, and exposes it as header fields + a table — "regenerate" and
/// "open the page" are the same code path, so there is no separate stale-cache concern to manage.
/// </summary>
public partial class ReportPageViewModel : ObservableObject
{
    private readonly ProjectSession _session;
    private readonly ReportEngine _engine;
    private readonly Page _page;

    [ObservableProperty]
    private string? _warningText;

    [ObservableProperty]
    private string? _title;

    public ObservableCollection<ReportHeaderField> HeaderFields { get; } = [];
    public ObservableCollection<ReportColumn> Columns { get; } = [];
    public ObservableCollection<IReadOnlyDictionary<string, object?>> Rows { get; } = [];

    public ReportPageViewModel(ProjectSession session, ReportEngine engine, Page page)
    {
        _session = session;
        _engine = engine;
        _page = page;
        Regenerate();
    }

    [RelayCommand]
    private void Regenerate()
    {
        var report = _session.GetGeneratedReportForPage(_page.Id);
        if (report is null)
        {
            WarningText = "This page has no associated generated report.";
            return;
        }

        try
        {
            var (layout, data, warning) = BuildData(report);
            WarningText = warning;
            Title = layout.Header?.Content.OfType<StaticTextRegion>().FirstOrDefault()?.Text ?? report.ReportKind;

            HeaderFields.Clear();
            foreach (var field in layout.Header?.Content.OfType<FieldRegion>() ?? [])
                HeaderFields.Add(new ReportHeaderField(field.Label, FormatValue(data.GetScalar(field.DataFieldKey))));

            var table = layout.Body.OfType<RepeatingTableRegion>().FirstOrDefault();
            Columns.Clear();
            Rows.Clear();
            if (table is not null)
            {
                foreach (var column in table.Columns)
                    Columns.Add(new ReportColumn(column.Header, column.DataFieldKey, column.Align));
                foreach (var row in data.GetTable(table.DataSourceKey))
                    Rows.Add(row);
            }
        }
        catch (Exception ex)
        {
            WarningText = $"Could not generate this report: {ex.Message}";
        }
    }

    private (ReportLayout Layout, ReportDataContext Data, string? Warning) BuildData(GeneratedReport report)
    {
        switch (report.ReportKind)
        {
            case ReportKinds.ConnectionList:
            {
                var cables = _session.GetAllCables();
                var data = ConnectionListReportBuilder.Build(
                    _session.GetAllConnections(), _session.GetAllConnectionEndsWithContext(), _session.GetAllDevices(),
                    _session.GetAllDevicePins(), _session.GetAllParts(), cables, GetAllCableCores(cables));
                var layout = _engine.FindLayout(ReportKinds.ConnectionList)
                    ?? throw new InvalidOperationException($"No '{ReportKinds.ConnectionList}' layout template is loaded.");
                return (layout, data, null);
            }
            case ReportKinds.Bom:
            {
                var mode = report.GroupingKey switch
                {
                    "Location" => BomGroupingMode.PerLocation,
                    "CableAssembly" => BomGroupingMode.PerCableAssembly,
                    _ => BomGroupingMode.PerProject,
                };
                var data = BomReportBuilder.Build(
                    _session.GetAllDevices(), _session.GetAllDevicePins(), _session.GetAllConnections(),
                    _session.GetAllConnectionEndsWithContext(), _session.GetAllCables(), _session.GetAllParts(), mode);
                var layout = _engine.FindLayout(ReportKinds.Bom)
                    ?? throw new InvalidOperationException($"No '{ReportKinds.Bom}' layout template is loaded.");
                return (layout, data, null);
            }
            case ReportKinds.CableOverview:
            {
                var cables = _session.GetAllCables();
                var data = CableOverviewReportBuilder.Build(
                    cables, GetAllCableCores(cables), _session.GetAllConnections(), _session.GetAllDevicePins(), _session.GetAllDevices());
                var layout = _engine.FindLayout(ReportKinds.CableOverview)
                    ?? throw new InvalidOperationException($"No '{ReportKinds.CableOverview}' layout template is loaded.");
                return (layout, data, null);
            }
            case ReportKinds.CableManufacturingSheet:
            {
                var cable = report.SourceEntityId is { } cableId ? _session.GetCable(cableId) : null;
                if (cable is null) throw new InvalidOperationException("This cable no longer exists.");

                var cores = _session.GetCableCores(cable.Id);
                var cableConnections = _session.GetAllConnections().Where(c => c.CableId == cable.Id).ToList();
                var result = CableManufacturingSheetReportBuilder.Build(
                    cable, cores, cableConnections, _session.GetAllConnectionEndsWithContext(),
                    _session.GetAllDevicePins(), _session.GetAllDevices(), _engine.Layouts);
                return (result.Layout, result.Data, result.Warning);
            }
            default:
                throw new InvalidOperationException($"Unknown report kind '{report.ReportKind}'.");
        }
    }

    private List<CableCore> GetAllCableCores(IReadOnlyList<Cable> cables) =>
        cables.SelectMany(c => _session.GetCableCores(c.Id)).ToList();

    private static string FormatValue(object? value) => value switch
    {
        null => string.Empty,
        double d => d.ToString("0.##"),
        _ => value.ToString() ?? string.Empty,
    };
}
