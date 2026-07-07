namespace Ecad.Rendering.Canvas;

/// <summary>
/// Transforms a symbol connection point's local (0..40 viewBox, see ADR-006) coordinate into the
/// world position it occupies once placed — the exact forward version of the center-rotate-mirror
/// transform SchematicCanvasRenderer.DrawPlacement already applies to the symbol's picture, applied
/// here to a single point instead. PlacementHitTester's rotation math is the inverse of this (world
/// click point -> local), used for hit-testing rather than rendering.
/// </summary>
public static class PlacementPinGeometry
{
    public static (double X, double Y) GetPinWorldPosition(double placementX, double placementY,
        int rotationDegrees, bool mirrored, double localX, double localY)
    {
        var centerX = placementX + 20;
        var centerY = placementY + 20;

        var dx = localX - 20;
        var dy = localY - 20;
        if (mirrored) dx = -dx;

        var angleRad = rotationDegrees * Math.PI / 180.0;
        var rx = dx * Math.Cos(angleRad) - dy * Math.Sin(angleRad);
        var ry = dx * Math.Sin(angleRad) + dy * Math.Cos(angleRad);

        return (centerX + rx, centerY + ry);
    }

    /// <summary>
    /// Transforms a connection point's local outward direction (0=right, 90=down, 180=left, 270=up)
    /// by the same mirror-then-rotate order GetPinWorldPosition applies to position. Mirroring flips
    /// left/right (reflects the direction across the vertical axis: 180 - d), then rotation adds the
    /// placement's rotation directly — both exact under the app's grid/90°-rotation constraints, so
    /// this always lands on one of 0/90/180/270, never an odd angle.
    /// </summary>
    public static double GetPinWorldDirection(int rotationDegrees, bool mirrored, double localDirection)
    {
        var afterMirror = mirrored ? 180 - localDirection : localDirection;
        return NormalizeAngle(afterMirror + rotationDegrees);
    }

    private static double NormalizeAngle(double angle)
    {
        var normalized = angle % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}
