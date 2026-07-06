using SkiaSharp;

namespace Ecad.Rendering.Canvas;

/// <summary>A placement's rendering data — the symbol's SKPicture (parsed once, kept live for the
/// window's lifetime so rotation/zoom stay crisp, unlike M4's pre-rasterized thumbnails).</summary>
public sealed record PlacementRenderInfo(long Id, string DeviceTag, double X, double Y, double Width, double Height, int RotationDegrees, bool Mirrored, SKPicture Picture);

/// <summary>Pure drawing logic for the schematic canvas — no WPF dependency, driven by SchematicPageWindow's PaintSurface handler.</summary>
public static class SchematicCanvasRenderer
{
    private static readonly SKColor GridColor = new(220, 220, 220);

    public static void Render(SKCanvas canvas, CanvasViewport viewport, int surfaceWidth, int surfaceHeight,
        IReadOnlyList<PlacementRenderInfo> placements, long? selectedPlacementId)
    {
        canvas.Clear(SKColors.White);
        DrawGrid(canvas, viewport, surfaceWidth, surfaceHeight);

        foreach (var placement in placements)
        {
            DrawPlacement(canvas, viewport, placement, isSelected: placement.Id == selectedPlacementId);
        }
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
    }
}
