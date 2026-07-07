namespace Ecad.App.Canvas;

/// <summary>
/// A wire on the schematic canvas — metadata only, no cached geometry. Its route is recomputed
/// fresh from its two pins' current world positions on every render (SchematicPageViewModel), which
/// is what makes "connection lines re-route when symbols move" (Section 6.1) work for free.
/// </summary>
public sealed class ConnectionViewItem
{
    public required long ConnectionId { get; init; }
    public required long FromDevicePinId { get; init; }
    public required long ToDevicePinId { get; init; }
    public string? WireNumber { get; set; }
}
