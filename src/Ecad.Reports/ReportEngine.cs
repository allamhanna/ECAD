using Ecad.Reports.LayoutSchema;

namespace Ecad.Reports;

/// <summary>
/// The facade Ecad.App holds onto: loads the report layout templates once, then looks one up by kind
/// (and, for the manufacturing sheet, by end-type Variant) for a report Builder + the ViewModel to use.
/// Reports render as a plain in-app data page (a table bound directly to the Builder's output) — no PDF
/// generation happens here; that stays a deferred, separately-licensed concern for a later milestone.
/// </summary>
public sealed class ReportEngine
{
    public IReadOnlyList<ReportLayout> Layouts { get; private set; } = [];
    public IReadOnlyList<string> LoadWarnings { get; private set; } = [];

    public void LoadTemplates(string folderPath)
    {
        var result = ReportLayoutLoader.LoadFromFolder(folderPath);
        Layouts = result.Layouts;
        LoadWarnings = result.Warnings;
    }

    public ReportLayout? FindLayout(string reportKind, string? variant = null) =>
        Layouts.FirstOrDefault(l => l.ReportKind == reportKind && l.Variant == variant);
}
