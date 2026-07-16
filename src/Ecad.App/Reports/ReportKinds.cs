using Ecad.Reports.Builders;

namespace Ecad.App.Reports;

/// <summary>
/// The well-known report kind + document-type-segment strings Ecad.App uses when generating/opening a
/// report page. ProjectSession keeps its own private duplicate of the CableManufacturingSheet string
/// (it cannot reference Ecad.Reports, per STRUCTURE.md's dependency direction) — keep both in sync by
/// hand if either ever changes.
/// </summary>
internal static class ReportKinds
{
    public const string ConnectionList = "ConnectionList";
    public const string Bom = "Bom";
    public const string CableOverview = "CableOverview";
    public const string CableManufacturingSheet = CableManufacturingSheetReportBuilder.ReportKind;
}

internal static class ReportDocumentTypeSegments
{
    public const string ConnectionList = "RPT-CONN";
    public const string Bom = "RPT-BOM";
    public const string CableOverview = "RPT-CAB";
    public const string CableManufacturingSheet = "RPT-F09";
}
