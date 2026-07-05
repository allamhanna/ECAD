using System.IO;
using System.Text.Json;

namespace Ecad.Rendering.Symbols;

/// <summary>One symbol's parsed metadata plus its raw SVG bytes, ready to rasterize or (later, M5) place.</summary>
public sealed record LoadedSymbol(SymbolDefinition Definition, string SvgFilePath, byte[] SvgBytes);

public sealed record SymbolLibraryLoadResult(IReadOnlyList<LoadedSymbol> Symbols, IReadOnlyList<string> Warnings);

/// <summary>
/// Scans a folder for "{Name}.symbol.json" + matching "{Name}.svg" pairs. Doesn't know or care
/// where the folder lives — Ecad.App resolves that (bundled under AppContext.BaseDirectory today).
/// </summary>
public static class SymbolLibraryLoader
{
    public static SymbolLibraryLoadResult LoadFromFolder(string folderPath)
    {
        var symbols = new List<LoadedSymbol>();
        var warnings = new List<string>();

        if (!Directory.Exists(folderPath))
        {
            warnings.Add($"Symbol library folder not found: '{folderPath}'.");
            return new SymbolLibraryLoadResult(symbols, warnings);
        }

        foreach (var jsonPath in Directory.EnumerateFiles(folderPath, "*.symbol.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var definition = JsonSerializer.Deserialize<SymbolDefinition>(File.ReadAllText(jsonPath))
                    ?? throw new InvalidOperationException("Empty or invalid JSON.");

                // "RelayCoil.symbol.json" -> strip ".json" -> "RelayCoil.symbol" -> strip ".symbol" -> "RelayCoil"
                var baseName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(jsonPath));
                var svgPath = Path.Combine(folderPath, baseName + ".svg");

                if (!File.Exists(svgPath))
                {
                    warnings.Add($"'{Path.GetFileName(jsonPath)}' has no matching '{Path.GetFileName(svgPath)}' — skipped.");
                    continue;
                }

                symbols.Add(new LoadedSymbol(definition, svgPath, File.ReadAllBytes(svgPath)));
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to load '{Path.GetFileName(jsonPath)}': {ex.Message}");
            }
        }

        return new SymbolLibraryLoadResult(symbols, warnings);
    }
}
