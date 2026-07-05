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
}
