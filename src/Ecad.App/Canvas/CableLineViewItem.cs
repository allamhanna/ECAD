namespace Ecad.App.Canvas;

/// <summary>
/// A cable definition line as shown on the schematic canvas — an independent, symbol-like entity with
/// its own absolute endpoints, mutable in place so drag feels live (same convention as
/// PlacementViewItem/DefinitionPointViewItem). CableTag is display-only, kept in sync with the Cable it
/// currently references.
/// </summary>
public sealed class CableLineViewItem
{
    public required long Id { get; init; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public long CableId { get; set; }
    public string CableTag { get; set; } = string.Empty;

    /// <summary>This line's currently-tracked crossings, refreshed whenever the line's geometry/cable
    /// changes — an orphaned crossing (ConnectionId null, its Connection was deleted elsewhere) is kept
    /// here too but simply never resolves to a render point.</summary>
    public List<CableLineCrossingViewItem> Crossings { get; set; } = [];
}

/// <summary>
/// One of a CableLineViewItem's crossings — independently selectable/rotatable/editable, same
/// mutable-in-place convention as DefinitionPointViewItem, even though its position isn't stored here
/// (it's resolved live against the wire's current route — see SchematicPageViewModel.BuildWiringRenderInfo).
/// Color/CrossSectionMm2/CoreNumber mirror the underlying CableCore row.
/// </summary>
public sealed class CableLineCrossingViewItem
{
    public required long Id { get; init; }
    public long? ConnectionId { get; set; }
    public long CableCoreId { get; set; }
    public int CoreNumber { get; set; }
    public string? Color { get; set; }
    public double? CrossSectionMm2 { get; set; }
    public int RotationDegrees { get; set; }
}
