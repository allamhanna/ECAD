namespace Ecad.Data.Import;

public sealed class EplanImportResult
{
    public int PartsAdded { get; set; }
    public int PartsUpdated { get; set; }
    public int PartsUnchanged { get; set; }
    public List<string> Warnings { get; } = [];
}
