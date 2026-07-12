namespace Ecad.App.Canvas;

/// <summary>
/// A definition point as shown on the schematic canvas — an independent, symbol-like entity with its
/// own absolute X/Y, mutable in place so drag feels live (same convention as PlacementViewItem). Its
/// identity and position don't depend on any Connection existing; ConnectionId is an optional,
/// non-load-bearing attachment mirrored onto that Connection's own WireNumber/Color/CrossSectionMm2
/// while set (see ProjectSession.AttachDefinitionPointToConnection).
/// </summary>
public sealed class DefinitionPointViewItem
{
    public required long Id { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
    public string? WireNumber { get; set; }
    public string? Color { get; set; }
    public double? CrossSectionMm2 { get; set; }
    public long? ConnectionId { get; set; }
    public int RotationDegrees { get; set; }
}
