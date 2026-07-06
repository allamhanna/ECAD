namespace Ecad.Rendering.Canvas;

/// <summary>Pan/zoom state for a schematic canvas, plus the world&lt;-&gt;screen conversions and grid snapping every mouse interaction needs.</summary>
public sealed class CanvasViewport
{
    public double PanX { get; set; }
    public double PanY { get; set; }
    public double Zoom { get; set; } = 1.0;
    public double GridSpacing { get; set; } = 20.0;

    public (double X, double Y) WorldToScreen(double worldX, double worldY) =>
        ((worldX + PanX) * Zoom, (worldY + PanY) * Zoom);

    public (double X, double Y) ScreenToWorld(double screenX, double screenY) =>
        (screenX / Zoom - PanX, screenY / Zoom - PanY);

    public (double X, double Y) SnapToGrid(double worldX, double worldY) =>
        (Math.Round(worldX / GridSpacing) * GridSpacing, Math.Round(worldY / GridSpacing) * GridSpacing);
}
