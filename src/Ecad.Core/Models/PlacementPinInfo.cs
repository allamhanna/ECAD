namespace Ecad.Core.Models;

/// <summary>One DevicePin a Placement exposes — enough for the canvas to resolve this pin's world
/// position (via the symbol's matching-named connection point) without a further DB round-trip.</summary>
public sealed record PlacementPinInfo(long DevicePinId, string Name);
