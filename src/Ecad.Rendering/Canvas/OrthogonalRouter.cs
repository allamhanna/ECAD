namespace Ecad.Rendering.Canvas;

/// <summary>
/// Simplest useful routing between two pin world positions (Section 9 of REQUIREMENTS.md: "start
/// with simple orthogonal routing; note upgrade path"). A straight segment when already aligned,
/// otherwise one horizontal-then-vertical bend. Exiting/entering in each pin's own Direction first
/// (the more EPLAN-like "hook out, jog over, hook in" look) is the noted upgrade path, not this pass.
/// </summary>
public static class OrthogonalRouter
{
    public static IReadOnlyList<WorldPoint> Route(WorldPoint from, WorldPoint to)
    {
        if (IsClose(from.X, to.X) || IsClose(from.Y, to.Y))
            return [from, to];

        return [from, new WorldPoint(to.X, from.Y), to];
    }

    private static bool IsClose(double a, double b) => Math.Abs(a - b) < 0.01;
}
