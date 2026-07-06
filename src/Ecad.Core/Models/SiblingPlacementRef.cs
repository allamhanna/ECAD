namespace Ecad.Core.Models;

/// <summary>A sibling placement of the same Device — the cross-reference display data (Section 5.4:
/// a relay coil placement lists its contacts' page locations and vice versa).</summary>
public sealed record SiblingPlacementRef(long PlacementId, long PageId, string PageLabel);
