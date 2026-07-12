namespace Ecad.Core.Models;

/// <summary>
/// One wire a CableLine currently crosses, and which CableCore it was assigned to. ConnectionId is
/// nullable: if the crossed wire's Connection is deleted elsewhere (e.g. auto-connect rewiring an
/// unrelated symbol move), this row survives as an orphan rather than disappearing — the CableLine and
/// its other crossings are unaffected.
/// </summary>
public class CableLineCrossing
{
    public long Id { get; set; }
    public long CableLineId { get; set; }
    public long? ConnectionId { get; set; }
    public long CableCoreId { get; set; }

    /// <summary>Purely cosmetic tick angle (R key, 90° per press) — never affects position or which wire this crossing represents.</summary>
    public int RotationDegrees { get; set; }
}
