using System.Text.Json;

namespace Ecad.Reports.LayoutSchema;

public sealed record ReportLayoutLoadResult(IReadOnlyList<ReportLayout> Layouts, IReadOnlyList<string> Warnings);

/// <summary>
/// Loads every *.json report template from a folder on disk — mirrors SymbolLibraryLoader.LoadFromFolder's
/// shape and its "collect warnings, don't crash on one bad file" tolerance, so a hand-edited template with
/// a typo degrades to a missing report rather than breaking every other report or the app itself
/// (REQUIREMENTS 5.9: "editable as files in Phase 1").
/// </summary>
public static class ReportLayoutLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static ReportLayoutLoadResult LoadFromFolder(string folderPath)
    {
        var layouts = new List<ReportLayout>();
        var warnings = new List<string>();

        if (!Directory.Exists(folderPath))
        {
            warnings.Add($"Report templates folder not found: {folderPath}");
            return new ReportLayoutLoadResult(layouts, warnings);
        }

        foreach (var filePath in Directory.GetFiles(folderPath, "*.json").OrderBy(f => f))
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var layout = JsonSerializer.Deserialize<ReportLayout>(json, Options);
                if (layout is null)
                {
                    warnings.Add($"{Path.GetFileName(filePath)}: deserialized to null, skipped.");
                    continue;
                }
                layouts.Add(layout);
            }
            catch (Exception ex)
            {
                warnings.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        return new ReportLayoutLoadResult(layouts, warnings);
    }
}
