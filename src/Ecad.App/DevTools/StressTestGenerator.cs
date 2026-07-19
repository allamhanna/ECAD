using System.IO;
using Ecad.Data;
using Ecad.Rendering.Symbols;

namespace Ecad.App.DevTools;

/// <summary>
/// M14 Part 3 (performance hardening): generates a synthetic page of Terminal placements/wires/
/// definition points/cable lines, well past the 500-element REQUIREMENTS target, so real render/
/// drag performance can actually be measured against something instead of guessed at. Built
/// entirely on ProjectSession's existing public API — no new session/repository methods needed.
///
/// Terminal's own two pins are a top pin (facing up) and a bottom pin (facing down) — meant for
/// stacking terminals vertically. Each of the Chains columns is its own vertical stack of
/// TerminalsPerChain terminals, wired bottom-pin-to-top-pin straight down the column: both pins
/// share the same X, so the route between them is a plain vertical segment with no bend, and
/// never crosses a neighboring terminal's body. CreateConnection doesn't itself check pin
/// direction/facing (that's the canvas's own click-to-draw auto-connect logic) — this generator
/// still deliberately keeps its wiring geometrically sensible rather than relying on that.
/// </summary>
public static class StressTestGenerator
{
    private const int Chains = 20;
    private const int TerminalsPerChain = 15;
    private const double ChainSpacing = 150;
    private const double TerminalSpacing = 150;

    /// <summary>Runs the whole generation as one transaction (ProjectSession.RunBatch) instead of
    /// ~750 separate auto-committed writes — the difference between this taking well under a second
    /// and the ~30-second freeze it used to cause.</summary>
    public static void Generate(ProjectSession session, long pageId) => session.RunBatch(() => GenerateCore(session, pageId));

    private static void GenerateCore(ProjectSession session, long pageId)
    {
        var symbolFolder = Path.Combine(AppContext.BaseDirectory, "SymbolLibrary");
        var loadResult = SymbolLibraryLoader.LoadFromFolder(symbolFolder);
        var terminal = loadResult.Symbols.First(s => s.Definition.Name == "Terminal");

        // connectionsByGap[gap][chain] = the connection wiring terminal `gap` to terminal `gap + 1`
        // within that chain — indexed this way (rather than per-chain) so cable lines can cross
        // several chains' worth of same-height wires below.
        var connectionsByGap = new List<long>[TerminalsPerChain - 1];
        for (var i = 0; i < connectionsByGap.Length; i++) connectionsByGap[i] = new List<long>();

        for (var chain = 0; chain < Chains; chain++)
        {
            var x = chain * ChainSpacing;
            long? previousPinTwoId = null;

            for (var row = 0; row < TerminalsPerChain; row++)
            {
                var y = row * TerminalSpacing;
                var placement = session.PlaceSymbol(pageId, terminal.Definition.Name, "Starter", terminal.SvgFilePath,
                    terminal.Definition.Category, ["1", "2"], x, y, "GEN", null, $"T{chain}_{row}");

                var pins = session.GetPlacementPins(placement.Id);
                var pinOne = pins.First(p => p.Name == "1").DevicePinId;
                var pinTwo = pins.First(p => p.Name == "2").DevicePinId;

                if (previousPinTwoId is { } fromPinId)
                {
                    var gap = row - 1;
                    var connection = session.CreateConnection(fromPinId, pinOne);
                    connectionsByGap[gap].Add(connection.Id);

                    if (gap % 2 == 0)
                    {
                        session.PlaceDefinitionPoint(pageId, x + 20, y - TerminalSpacing + 95,
                            $"W{chain}_{gap}", gap % 4 == 0 ? "Red" : "Blue", 1.5, connection.Id);
                    }
                }

                previousPinTwoId = pinTwo;
            }
        }

        for (var gap = 0; gap < connectionsByGap.Length; gap++)
        {
            var y = gap * TerminalSpacing + 95;
            var firstHalf = connectionsByGap[gap].Take(Chains / 2).ToList();
            var secondHalf = connectionsByGap[gap].Skip(Chains / 2).ToList();

            session.DrawCableLine(pageId, 0, y, (Chains / 2) * ChainSpacing, y, $"CBL-G{gap}-A", firstHalf);
            session.DrawCableLine(pageId, (Chains / 2) * ChainSpacing, y, Chains * ChainSpacing, y, $"CBL-G{gap}-B", secondHalf);
        }
    }
}
