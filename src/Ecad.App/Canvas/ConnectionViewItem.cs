namespace Ecad.App.Canvas;

/// <summary>
/// A wire on the schematic canvas — just the two pins it joins, no other metadata or cached geometry.
/// Its route is recomputed fresh from its two pins' current world positions on every render
/// (SchematicPageViewModel), which is what makes "connection lines re-route when symbols move"
/// (Section 6.1) work for free. Any displayable data (wire number/color/cross-section) lives on a
/// DefinitionPoint instead — an independent entity, not a property of the connection itself.
/// </summary>
public sealed class ConnectionViewItem
{
    public required long ConnectionId { get; init; }
    public required long FromDevicePinId { get; init; }
    public required long ToDevicePinId { get; init; }
}
