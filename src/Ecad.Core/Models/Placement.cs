namespace Ecad.Core.Models;

/// <summary>A symbol instance of a Device on a Page.</summary>
public class Placement
{
    public long Id { get; set; }
    public long DeviceId { get; set; }
    public long PageId { get; set; }
    public long SymbolId { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public int RotationDegrees { get; set; }
    public bool Mirrored { get; set; }
    public string? Variant { get; set; }
}
