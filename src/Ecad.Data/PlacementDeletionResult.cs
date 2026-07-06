namespace Ecad.Data;

/// <summary>
/// What happened when a Placement was deleted — enough for a caller (Ecad.App's DeleteCommand) to
/// undo correctly. If DeviceDeleted is true, this was the Device's last Placement and the whole
/// Device is gone too (undo must recreate a new Device). Otherwise sibling placements/the Device
/// survive (undo just needs a new Placement on the same, still-existing Device).
/// </summary>
public sealed record PlacementDeletionResult(bool DeviceDeleted, long DeviceId, string DeviceTag,
    string? Function, string? Location, IReadOnlyList<string> PinNames);
