using Ecad.Core.Enums;

namespace Ecad.Core.Models;

/// <summary>One parts-library import run. Part.SourceImportBatchId points back to the run that last touched it.</summary>
public class ImportBatch
{
    public long Id { get; set; }
    public ImportSourceType SourceType { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public DateTimeOffset ImportedAtUtc { get; set; }
    public int PartsAdded { get; set; }
    public int PartsUpdated { get; set; }
    public int PartsUnchanged { get; set; }
}
