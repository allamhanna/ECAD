namespace Ecad.Core.Models;

/// <summary>A Placement joined with its Device tag and Symbol info — the read-shape the schematic canvas needs to render a page.</summary>
public class PlacementWithSymbol
{
    public long PlacementId { get; set; }
    public long DeviceId { get; set; }
    public string DeviceTag { get; set; } = string.Empty;
    public string? FunctionSegment { get; set; }
    public string? LocationSegment { get; set; }
    public long PageId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public int RotationDegrees { get; set; }
    public bool Mirrored { get; set; }
    public string SymbolName { get; set; } = string.Empty;
    public string? SymbolSvgFilePath { get; set; }
    public IReadOnlyList<SiblingPlacementRef> Siblings { get; set; } = [];
    public IReadOnlyList<PlacementPinInfo> Pins { get; set; } = [];
}
