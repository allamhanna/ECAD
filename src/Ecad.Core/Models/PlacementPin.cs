namespace Ecad.Core.Models;

/// <summary>
/// Which DevicePin a given Placement exposes. This join is the cross-reference mechanism: a
/// relay coil placement and its contact placements are different Placements of the same Device,
/// each exposing a different subset of DevicePins.
/// </summary>
public class PlacementPin
{
    public long Id { get; set; }
    public long PlacementId { get; set; }
    public long DevicePinId { get; set; }
}
