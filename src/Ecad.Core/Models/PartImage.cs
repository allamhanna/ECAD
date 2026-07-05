namespace Ecad.Core.Models;

/// <summary>A cached preview image for a Part, stored as a BLOB so the database stays self-contained (see ADR-005).</summary>
public class PartImage
{
    public long Id { get; set; }
    public long PartId { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public byte[] ImageData { get; set; } = [];
}
