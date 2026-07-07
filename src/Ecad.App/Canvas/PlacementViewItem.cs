using Ecad.Core.Models;
using SkiaSharp;

namespace Ecad.App.Canvas;

/// <summary>A placement as shown on the schematic canvas — mutable in place so drag/rotate feel live, with the parsed SKPicture cached alongside it.</summary>
public sealed class PlacementViewItem
{
    public required long PlacementId { get; init; }
    public required long DeviceId { get; init; }
    public required string DeviceTag { get; set; }
    public string? Function { get; set; }
    public string? Location { get; set; }
    public required string SymbolName { get; init; }
    public required SKPicture Picture { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
    public int RotationDegrees { get; set; }
    public bool Mirrored { get; set; }
    public IReadOnlyList<string> SiblingPageLabels { get; set; } = [];

    /// <summary>The other placements of this Device (Section 5.4 cross-reference) — kept alongside the
    /// display-only SiblingPageLabels so Ctrl+Click navigation has an actual PageId/PlacementId to jump to.</summary>
    public IReadOnlyList<SiblingPlacementRef> Siblings { get; set; } = [];

    /// <summary>The DevicePins this placement exposes — resolved to world positions (via the symbol's
    /// matching-named connection points) for wire rendering/hit-testing (M7).</summary>
    public IReadOnlyList<PlacementPinInfo> Pins { get; set; } = [];

    // Every M4 starter symbol shares the 0..40 viewBox convention (ADR-006) — placements occupy one fixed world footprint.
    public const double Width = 40;
    public const double Height = 40;
}
