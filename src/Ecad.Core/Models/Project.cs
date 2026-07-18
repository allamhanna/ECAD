namespace Ecad.Core.Models;

public class Project
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Customer { get; set; }
    public string? ProjectNumber { get; set; }
    public string? Revision { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>JSON: which IEC 81346 tag segments are in use for this project.</summary>
    public string? PageStructureSettingsJson { get; set; }

    /// <summary>JSON: wire/device numbering configuration.</summary>
    public string? NumberingSettingsJson { get; set; }

    /// <summary>JSON-serialized Ecad.App.ViewModels.PageNavigatorSettings — the Page Navigator's
    /// chosen grouping (Function/Location/Document Type/none). A UI display preference, not project
    /// content, but kept per-project since different projects may want a different default view;
    /// deliberately its own column rather than reusing PageStructureSettingsJson above, which is
    /// earmarked for a different, still-unbuilt concern (tag segment usage).</summary>
    public string? PageNavigatorSettingsJson { get; set; }
}
