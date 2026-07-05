namespace Ecad.Core.Models;

/// <summary>A node in the parts classification tree (e.g. Electrical Engineering &gt; Connections &gt; Ferrules).</summary>
public class Classification
{
    public long Id { get; set; }
    public long? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
}
