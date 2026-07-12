using SkiaSharp;

namespace Ecad.Rendering.Canvas;

/// <summary>A placement's rendering data — the symbol's SKPicture (parsed once, kept live for the
/// window's lifetime so rotation/zoom stay crisp, unlike M4's pre-rasterized thumbnails).
/// SiblingPageLabels is empty for a single-placement device — cross-reference text (Section 5.4)
/// only shows up once a Device actually has more than one Placement.</summary>
public sealed record PlacementRenderInfo(long Id, string DeviceTag, double X, double Y, double Width, double Height,
    int RotationDegrees, bool Mirrored, SKPicture Picture, IReadOnlyList<string> SiblingPageLabels);

/// <summary>A DevicePin's current world position — drawn as a small marker so there's something to click (M7).</summary>
public sealed record PinRenderInfo(long DevicePinId, WorldPoint Position);

/// <summary>A Connection's already-routed path (see OrthogonalRouter) — a plain line only; a
/// connection has no independent selectable identity and carries no definition-point data of its own
/// (see DefinitionPointRenderInfo, a top-level, independent entity).</summary>
public sealed record WireRenderInfo(long ConnectionId, IReadOnlyList<WorldPoint> Route);

/// <summary>A definition point — an independent, symbol-like canvas entity with its own absolute
/// position, drawn every frame exactly like a PlacementRenderInfo regardless of whether any wire is
/// nearby. Optionally attached to a Connection (not represented here — rendering doesn't need to know).</summary>
public sealed record DefinitionPointRenderInfo(long Id, double X, double Y, int RotationDegrees, string? WireNumber, string? Color, double? CrossSectionMm2);

/// <summary>A cable definition line — an independent, symbol-like canvas entity with its own absolute
/// endpoints, drawn every frame regardless of which wires it currently crosses (rendering doesn't
/// recompute crossings; the ViewModel already resolved them into CableLineCrossingRenderInfo).</summary>
public sealed record CableLineRenderInfo(long Id, double X1, double Y1, double X2, double Y2, string CableTag);

/// <summary>Where a CableLine currently, geometrically crosses one of its assigned wires — resolved by
/// the ViewModel each frame (a pure, read-only lookup against the wire's live route), not stored. If a
/// wire has since moved away and no longer crosses the line, no render info is produced for it that
/// frame; the underlying assignment is untouched. Id is the underlying CableLineCrossing's own row id,
/// so it can be independently selected/rotated just like a DefinitionPoint.</summary>
public sealed record CableLineCrossingRenderInfo(long Id, double X, double Y, int RotationDegrees,
    string CableTag, int CoreNumber, string? Color, double? CrossSectionMm2);

/// <summary>Everything M7 adds to a page's render pass, bundled to keep Render's own signature
/// manageable. A wire/Connection itself is never selectable or highlighted — only a definition point
/// is (SelectedDefinitionPointIds, keyed by the point's own Id); a connection has no independent
/// identity to select, matching ADR-015's "no standalone delete" for the Grid Editor, extended to the
/// canvas here.</summary>
public sealed record WiringRenderInfo(IReadOnlyList<PinRenderInfo> Pins, IReadOnlyList<WireRenderInfo> Wires,
    IReadOnlyList<DefinitionPointRenderInfo> DefinitionPoints, IReadOnlyCollection<long> SelectedDefinitionPointIds,
    IReadOnlyList<WorldPoint> Junctions, IReadOnlyList<WorldPoint>? WireDrawPreviewRoute,
    IReadOnlyList<CableLineRenderInfo> CableLines, IReadOnlyCollection<long> SelectedCableLineIds,
    IReadOnlyList<CableLineCrossingRenderInfo> CableLineCrossings, IReadOnlyCollection<long> SelectedCableLineCrossingIds);

/// <summary>An in-progress rubber-band selection drag, in screen coordinates — Start is where the
/// right button first went down, Current tracks the live mouse position.</summary>
public sealed record RubberBandRenderInfo(double StartX, double StartY, double CurrentX, double CurrentY);

/// <summary>Pure drawing logic for the schematic canvas — no WPF dependency, driven by SchematicPageWindow's PaintSurface handler.</summary>
public static class SchematicCanvasRenderer
{
    private static readonly SKColor GridColor = new(220, 220, 220);
    private static readonly SKColor PinColor = new(160, 30, 30);
    private static readonly SKColor WireColor = new(30, 30, 30);
    private static readonly SKColor JunctionColor = new(30, 30, 30);
    private static readonly SKColor CableLineColor = new(140, 90, 20);
    private static readonly SKColor DefinitionPointColor = new(200, 30, 30); // red — both a wire's definition point and a cable crossing's tick

    public static void Render(SKCanvas canvas, CanvasViewport viewport, int surfaceWidth, int surfaceHeight,
        IReadOnlyList<PlacementRenderInfo> placements, IReadOnlyCollection<long> selectedPlacementIds, WiringRenderInfo wiring,
        RubberBandRenderInfo? rubberBand = null)
    {
        canvas.Clear(SKColors.White);
        DrawGrid(canvas, viewport, surfaceWidth, surfaceHeight);
        DrawWires(canvas, viewport, wiring.Wires);
        DrawJunctions(canvas, viewport, wiring.Junctions);

        foreach (var placement in placements)
        {
            DrawPlacement(canvas, viewport, placement, isSelected: selectedPlacementIds.Contains(placement.Id));
        }

        foreach (var definitionPoint in wiring.DefinitionPoints)
        {
            DrawDefinitionPoint(canvas, viewport, definitionPoint, isSelected: wiring.SelectedDefinitionPointIds.Contains(definitionPoint.Id));
        }

        foreach (var cableLine in wiring.CableLines)
        {
            DrawCableLine(canvas, viewport, cableLine, isSelected: wiring.SelectedCableLineIds.Contains(cableLine.Id));
        }
        foreach (var crossing in wiring.CableLineCrossings)
        {
            DrawCableLineCrossing(canvas, viewport, crossing, isSelected: wiring.SelectedCableLineCrossingIds.Contains(crossing.Id));
        }

        DrawPins(canvas, viewport, wiring.Pins);
        if (wiring.WireDrawPreviewRoute is { Count: > 0 } preview) DrawWireDrawPreview(canvas, viewport, preview);
        if (rubberBand is not null) DrawRubberBand(canvas, rubberBand);
    }

    private static void DrawGrid(SKCanvas canvas, CanvasViewport viewport, int width, int height)
    {
        var spacingScreen = viewport.GridSpacing * viewport.Zoom;
        if (spacingScreen < 4) return; // too dense to be worth drawing

        // Dots, not lines: GridSpacing only ever affects where these dots land and where
        // CanvasViewport.SnapToGrid rounds a new/dragged position to — a Placement's own X/Y is
        // always stored in absolute world units, so changing this at any time (even a large project
        // with existing parts) never moves anything already placed, only the grid's own density.
        using var paint = new SKPaint { Color = GridColor, IsAntialias = true, Style = SKPaintStyle.Fill };
        const float dotRadius = 1.5f;

        var (worldLeft, worldTop) = viewport.ScreenToWorld(0, 0);
        var startX = Math.Floor(worldLeft / viewport.GridSpacing) * viewport.GridSpacing;
        var startY = Math.Floor(worldTop / viewport.GridSpacing) * viewport.GridSpacing;

        for (var worldX = startX; ; worldX += viewport.GridSpacing)
        {
            var (screenX, _) = viewport.WorldToScreen(worldX, 0);
            if (screenX > width) break;

            for (var worldY = startY; ; worldY += viewport.GridSpacing)
            {
                var (_, screenY) = viewport.WorldToScreen(0, worldY);
                if (screenY > height) break;
                canvas.DrawCircle((float)screenX, (float)screenY, dotRadius, paint);
            }
        }
    }

    private static void DrawPlacement(SKCanvas canvas, CanvasViewport viewport, PlacementRenderInfo placement, bool isSelected)
    {
        var (screenX, screenY) = viewport.WorldToScreen(placement.X, placement.Y);
        var screenWidth = placement.Width * viewport.Zoom;
        var screenHeight = placement.Height * viewport.Zoom;

        canvas.Save();
        canvas.Translate((float)(screenX + screenWidth / 2), (float)(screenY + screenHeight / 2));
        canvas.RotateDegrees(placement.RotationDegrees);
        if (placement.Mirrored) canvas.Scale(-1, 1);

        // The symbol's SKPicture is authored in its own fixed viewBox (ADR-006's 0..40 convention)
        // — scale it to fill the placement's world footprint at the current zoom.
        var pictureBounds = placement.Picture.CullRect;
        if (pictureBounds.Width > 0 && pictureBounds.Height > 0)
        {
            canvas.Scale((float)(screenWidth / pictureBounds.Width), (float)(screenHeight / pictureBounds.Height));
        }
        canvas.Translate(-pictureBounds.Width / 2, -pictureBounds.Height / 2);
        canvas.DrawPicture(placement.Picture);
        canvas.Restore();

        if (isSelected)
        {
            using var selectionPaint = new SKPaint { Color = SKColors.DodgerBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
            canvas.DrawRect((float)screenX - 2, (float)screenY - 2, (float)screenWidth + 4, (float)screenHeight + 4, selectionPaint);
        }

        using var textPaint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var font = new SKFont { Size = 12 };
        canvas.DrawText(placement.DeviceTag, (float)screenX, (float)(screenY - 4), font, textPaint);

        if (placement.SiblingPageLabels.Count > 0)
        {
            using var crossRefPaint = new SKPaint { Color = SKColors.DarkSlateGray, IsAntialias = true };
            using var crossRefFont = new SKFont { Size = 9 };
            var crossRefText = "-> " + string.Join(", ", placement.SiblingPageLabels.Select(label => "Pg " + label));
            canvas.DrawText(crossRefText, (float)screenX, (float)(screenY + screenHeight + 11), crossRefFont, crossRefPaint);
        }
    }

    private static void DrawPins(SKCanvas canvas, CanvasViewport viewport, IReadOnlyList<PinRenderInfo> pins)
    {
        using var pinPaint = new SKPaint { Color = PinColor, IsAntialias = true, Style = SKPaintStyle.Fill };
        const float radius = 3f;

        foreach (var pin in pins)
        {
            var (screenX, screenY) = viewport.WorldToScreen(pin.Position.X, pin.Position.Y);
            canvas.DrawCircle((float)screenX, (float)screenY, radius, pinPaint);
        }
    }

    private static void DrawWires(SKCanvas canvas, CanvasViewport viewport, IReadOnlyList<WireRenderInfo> wires)
    {
        // The wire line itself never highlights — a connection has no independent selectable identity
        // (see WiringRenderInfo's own doc comment); only a definition point's tick does.
        using var wirePaint = new SKPaint { Color = WireColor, StrokeWidth = 1.5f, Style = SKPaintStyle.Stroke, IsAntialias = true };

        foreach (var wire in wires)
            DrawRoute(canvas, viewport, wire.Route, wirePaint);
    }

    private static void DrawDefinitionPoint(SKCanvas canvas, CanvasViewport viewport, DefinitionPointRenderInfo definitionPoint, bool isSelected)
    {
        var (screenX, screenY) = viewport.WorldToScreen(definitionPoint.X, definitionPoint.Y);
        var tickColor = isSelected ? SKColors.DodgerBlue : DefinitionPointColor;
        DrawDefinitionPointGlyph(canvas, screenX, screenY, definitionPoint.RotationDegrees, tickColor, definitionPoint.WireNumber, definitionPoint.Color, definitionPoint.CrossSectionMm2);
    }

    /// <summary>The diagonal-tick-plus-label glyph shared by wire definition points and cable line
    /// crossings — same visual language for "something was assigned here" across both features.
    /// RotationDegrees only spins the tick itself (R key, 90° per press); the label always stays
    /// upright at a fixed offset for readability, the same way DrawPlacement's tag text never rotates
    /// with the symbol picture.</summary>
    private static void DrawDefinitionPointGlyph(SKCanvas canvas, double screenX, double screenY, int rotationDegrees, SKColor tickColor,
        string? wireNumber, string? color, double? crossSectionMm2)
    {
        const float tickHalfLength = 5f;

        using (var tickPaint = new SKPaint { Color = tickColor, StrokeWidth = 1.5f, Style = SKPaintStyle.Stroke, IsAntialias = true })
        {
            canvas.Save();
            canvas.Translate((float)screenX, (float)screenY);
            canvas.RotateDegrees(rotationDegrees);
            canvas.DrawLine(-tickHalfLength, tickHalfLength, tickHalfLength, -tickHalfLength, tickPaint);
            canvas.Restore();
        }

        var crossSectionAndColor = string.Join(" ", new[]
        {
            crossSectionMm2 is { } crossSection ? $"{crossSection}mm²" : null,
            color,
        }.Where(s => !string.IsNullOrEmpty(s)));

        var labelLines = new List<string>();
        if (!string.IsNullOrEmpty(wireNumber)) labelLines.Add(wireNumber);
        if (crossSectionAndColor.Length > 0) labelLines.Add(crossSectionAndColor);
        if (labelLines.Count == 0) return;

        using var labelPaint = new SKPaint { Color = tickColor, IsAntialias = true };
        using var labelFont = new SKFont { Size = 9 };
        const float lineHeight = 11f;
        for (var i = 0; i < labelLines.Count; i++)
            canvas.DrawText(labelLines[i], (float)screenX + tickHalfLength + 2, (float)screenY - tickHalfLength + i * lineHeight, labelFont, labelPaint);
    }

    private static void DrawCableLine(SKCanvas canvas, CanvasViewport viewport, CableLineRenderInfo cableLine, bool isSelected)
    {
        var (x1, y1) = viewport.WorldToScreen(cableLine.X1, cableLine.Y1);
        var (x2, y2) = viewport.WorldToScreen(cableLine.X2, cableLine.Y2);

        using var linePaint = new SKPaint
        {
            Color = isSelected ? SKColors.DodgerBlue : CableLineColor,
            StrokeWidth = 2f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash([6, 3], 0),
        };
        canvas.DrawLine((float)x1, (float)y1, (float)x2, (float)y2, linePaint);

        using var labelPaint = new SKPaint { Color = linePaint.Color, IsAntialias = true };
        using var labelFont = new SKFont { Size = 10 };
        canvas.DrawText(cableLine.CableTag, (float)x1 + 4, (float)y1 - 4, labelFont, labelPaint);
    }

    private static void DrawCableLineCrossing(SKCanvas canvas, CanvasViewport viewport, CableLineCrossingRenderInfo crossing, bool isSelected)
    {
        var (screenX, screenY) = viewport.WorldToScreen(crossing.X, crossing.Y);
        var tickColor = isSelected ? SKColors.DodgerBlue : DefinitionPointColor;
        DrawDefinitionPointGlyph(canvas, screenX, screenY, crossing.RotationDegrees, tickColor,
            $"{crossing.CableTag}/{crossing.CoreNumber}", crossing.Color, crossing.CrossSectionMm2);
    }

    private static void DrawJunctions(SKCanvas canvas, CanvasViewport viewport, IReadOnlyList<WorldPoint> junctions)
    {
        using var junctionPaint = new SKPaint { Color = JunctionColor, IsAntialias = true, Style = SKPaintStyle.Fill };
        const float radius = 3.5f;

        foreach (var point in junctions)
        {
            var (screenX, screenY) = viewport.WorldToScreen(point.X, point.Y);
            canvas.DrawCircle((float)screenX, (float)screenY, radius, junctionPaint);
        }
    }

    private static void DrawWireDrawPreview(SKCanvas canvas, CanvasViewport viewport, IReadOnlyList<WorldPoint> route)
    {
        using var previewPaint = new SKPaint
        {
            Color = SKColors.DodgerBlue,
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash([4, 4], 0),
        };
        DrawRoute(canvas, viewport, route, previewPaint);
    }

    private static void DrawRubberBand(SKCanvas canvas, RubberBandRenderInfo rubberBand)
    {
        var left = (float)Math.Min(rubberBand.StartX, rubberBand.CurrentX);
        var top = (float)Math.Min(rubberBand.StartY, rubberBand.CurrentY);
        var right = (float)Math.Max(rubberBand.StartX, rubberBand.CurrentX);
        var bottom = (float)Math.Max(rubberBand.StartY, rubberBand.CurrentY);

        using var fillPaint = new SKPaint { Color = SKColors.DodgerBlue.WithAlpha(40), Style = SKPaintStyle.Fill };
        canvas.DrawRect(left, top, right - left, bottom - top, fillPaint);

        using var strokePaint = new SKPaint
        {
            Color = SKColors.DodgerBlue,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash([4, 4], 0),
        };
        canvas.DrawRect(left, top, right - left, bottom - top, strokePaint);
    }

    private static void DrawRoute(SKCanvas canvas, CanvasViewport viewport, IReadOnlyList<WorldPoint> route, SKPaint paint)
    {
        for (var i = 0; i < route.Count - 1; i++)
        {
            var (x1, y1) = viewport.WorldToScreen(route[i].X, route[i].Y);
            var (x2, y2) = viewport.WorldToScreen(route[i + 1].X, route[i + 1].Y);
            canvas.DrawLine((float)x1, (float)y1, (float)x2, (float)y2, paint);
        }
    }
}
