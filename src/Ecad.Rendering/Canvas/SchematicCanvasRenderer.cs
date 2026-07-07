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

/// <summary>A Connection's already-routed path (see OrthogonalRouter) plus its wire number, ready to draw (M7).</summary>
public sealed record WireRenderInfo(long ConnectionId, string? WireNumber, IReadOnlyList<WorldPoint> Route);

/// <summary>Everything M7 adds to a page's render pass, bundled to keep Render's own signature manageable.</summary>
public sealed record WiringRenderInfo(IReadOnlyList<PinRenderInfo> Pins, IReadOnlyList<WireRenderInfo> Wires,
    long? SelectedConnectionId, IReadOnlyList<WorldPoint> Junctions, IReadOnlyList<WorldPoint>? WireDrawPreviewRoute);

/// <summary>Pure drawing logic for the schematic canvas — no WPF dependency, driven by SchematicPageWindow's PaintSurface handler.</summary>
public static class SchematicCanvasRenderer
{
    private static readonly SKColor GridColor = new(220, 220, 220);
    private static readonly SKColor PinColor = new(160, 30, 30);
    private static readonly SKColor WireColor = new(30, 30, 30);
    private static readonly SKColor JunctionColor = new(30, 30, 30);

    public static void Render(SKCanvas canvas, CanvasViewport viewport, int surfaceWidth, int surfaceHeight,
        IReadOnlyList<PlacementRenderInfo> placements, long? selectedPlacementId, WiringRenderInfo wiring)
    {
        canvas.Clear(SKColors.White);
        DrawGrid(canvas, viewport, surfaceWidth, surfaceHeight);
        DrawWires(canvas, viewport, wiring.Wires, wiring.SelectedConnectionId);
        DrawJunctions(canvas, viewport, wiring.Junctions);

        foreach (var placement in placements)
        {
            DrawPlacement(canvas, viewport, placement, isSelected: placement.Id == selectedPlacementId);
        }

        DrawPins(canvas, viewport, wiring.Pins);
        if (wiring.WireDrawPreviewRoute is { Count: > 0 } preview) DrawWireDrawPreview(canvas, viewport, preview);
    }

    private static void DrawGrid(SKCanvas canvas, CanvasViewport viewport, int width, int height)
    {
        var spacingScreen = viewport.GridSpacing * viewport.Zoom;
        if (spacingScreen < 4) return; // too dense to be worth drawing

        using var paint = new SKPaint { Color = GridColor, StrokeWidth = 1, IsAntialias = false };

        var (worldLeft, worldTop) = viewport.ScreenToWorld(0, 0);
        var startX = Math.Floor(worldLeft / viewport.GridSpacing) * viewport.GridSpacing;
        var startY = Math.Floor(worldTop / viewport.GridSpacing) * viewport.GridSpacing;

        for (var worldX = startX; ; worldX += viewport.GridSpacing)
        {
            var (screenX, _) = viewport.WorldToScreen(worldX, 0);
            if (screenX > width) break;
            canvas.DrawLine((float)screenX, 0, (float)screenX, height, paint);
        }

        for (var worldY = startY; ; worldY += viewport.GridSpacing)
        {
            var (_, screenY) = viewport.WorldToScreen(0, worldY);
            if (screenY > height) break;
            canvas.DrawLine(0, (float)screenY, width, (float)screenY, paint);
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

    private static void DrawWires(SKCanvas canvas, CanvasViewport viewport, IReadOnlyList<WireRenderInfo> wires, long? selectedConnectionId)
    {
        foreach (var wire in wires)
        {
            var isSelected = wire.ConnectionId == selectedConnectionId;
            using var wirePaint = new SKPaint
            {
                Color = isSelected ? SKColors.DodgerBlue : WireColor,
                StrokeWidth = isSelected ? 2.5f : 1.5f,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true,
            };

            DrawRoute(canvas, viewport, wire.Route, wirePaint);

            if (string.IsNullOrEmpty(wire.WireNumber) || wire.Route.Count == 0) continue;

            var mid = wire.Route[wire.Route.Count / 2];
            var (midScreenX, midScreenY) = viewport.WorldToScreen(mid.X, mid.Y);
            using var numberPaint = new SKPaint { Color = WireColor, IsAntialias = true };
            using var numberFont = new SKFont { Size = 9 };
            canvas.DrawText(wire.WireNumber, (float)midScreenX + 3, (float)midScreenY - 3, numberFont, numberPaint);
        }
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
