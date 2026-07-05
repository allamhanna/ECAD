namespace Ecad.Core.Models;

/// <summary>Sub-assembly composition: this part includes another part (EPLAN assemblyposition/accessorylist).</summary>
public class PartAccessory
{
    public long Id { get; set; }
    public long PartId { get; set; }
    public string AccessoryPartExternalKey { get; set; } = string.Empty;
    public int Pos { get; set; }
}
