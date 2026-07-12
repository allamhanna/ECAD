namespace Ecad.Core.Models;

/// <summary>
/// A wire's user-placed definition point — the diagonal tick that carries and displays a wire's
/// number/color/cross-section. An independent, symbol-like canvas entity with its own absolute
/// position: it survives its attached Connection being deleted and recreated (which auto-connect does
/// on nearly every symbol move), and can exist with no Connection at all (a free-floating point).
/// ConnectionId is optional and non-load-bearing for the point's own existence — while set, its
/// WireNumber/Color/CrossSectionMm2 are mirrored onto that Connection's own columns so Grid Editor/
/// Terminations (which read those columns directly) reflect it live.
/// </summary>
public class DefinitionPoint
{
    public long Id { get; set; }
    public long PageId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public string? WireNumber { get; set; }
    public string? Color { get; set; }
    public double? CrossSectionMm2 { get; set; }
    public long? ConnectionId { get; set; }

    /// <summary>Purely cosmetic tick angle (R key, 90° per press) — never affects position or attachment.</summary>
    public int RotationDegrees { get; set; }
}
