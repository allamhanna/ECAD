namespace Ecad.Core.Models;

/// <summary>
/// Links a report kind (+ optional source entity/grouping) to the Page row hosting its on-screen
/// preview (M12: Reports engine). Regenerating a report looks up the existing row by
/// (ReportKind, SourceEntityId, GroupingKey) and reuses its PageId rather than creating a duplicate —
/// this identity is how Section 6.4's "no page-number collisions" is actually satisfied.
/// </summary>
public class GeneratedReport
{
    public long Id { get; set; }
    public long PageId { get; set; }
    public string ReportKind { get; set; } = string.Empty;

    /// <summary>A Cable.Id for a manufacturing-sheet page; null for every other report kind.</summary>
    public long? SourceEntityId { get; set; }

    /// <summary>Discriminates BOM grouping mode ("Project" / "Location:&lt;segment&gt;" / "CableAssembly");
    /// null for report kinds with no grouping choice.</summary>
    public string? GroupingKey { get; set; }

    public DateTimeOffset GeneratedAtUtc { get; set; }
}
