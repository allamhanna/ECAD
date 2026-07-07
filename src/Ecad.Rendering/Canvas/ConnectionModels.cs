namespace Ecad.Rendering.Canvas;

/// <summary>A world-space point. Exact-equality-comparable by value — safe here because every point
/// this project computes is grid-aligned (multiples of the 20-unit grid) and derived deterministically
/// from placement position + fixed symbol-local offsets, not accumulated through repeated transforms.</summary>
public readonly record struct WorldPoint(double X, double Y);

/// <summary>A DevicePin's current world position and outward-facing direction (0=right, 90=down,
/// 180=left, 270=up — already transformed by its placement's rotation/mirror), for auto-connect/junction detection.</summary>
public sealed record PinPosition(long DevicePinId, WorldPoint Position, double Direction);

/// <summary>An existing Connection's endpoints and already-routed path, for auto-connect/junction detection.</summary>
public sealed record ExistingConnection(long ConnectionId, long FromDevicePinId, long ToDevicePinId, IReadOnlyList<WorldPoint> Route);
