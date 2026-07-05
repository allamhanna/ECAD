using Ecad.Rendering.Symbols;
using Xunit;

namespace Ecad.Rendering.Tests;

public class SymbolLibraryLoaderTests
{
    private const string ValidSvg = """<svg xmlns="http://www.w3.org/2000/svg" width="40" height="40" viewBox="0 0 40 40"><rect x="5" y="5" width="30" height="30"/></svg>""";

    private const string ValidJson = """
        {
          "name": "RelayCoil",
          "category": "Coils",
          "connectionPoints": [
            { "pin": "A1", "x": 0, "y": 20, "direction": 180 },
            { "pin": "A2", "x": 40, "y": 20, "direction": 0 }
          ],
          "textPlaceholders": [
            { "kind": "Tag", "x": 20, "y": 5, "anchor": "middle" }
          ],
          "variants": [
            { "name": "Default", "rotationDegrees": 0, "mirrored": false }
          ]
        }
        """;

    [Fact]
    public void LoadFromFolder_ValidSymbol_LoadsDefinitionAndSvgBytes()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "RelayCoil.symbol.json"), ValidJson);
        File.WriteAllText(Path.Combine(dir.Path, "RelayCoil.svg"), ValidSvg);

        var result = SymbolLibraryLoader.LoadFromFolder(dir.Path);

        Assert.Empty(result.Warnings);
        var symbol = Assert.Single(result.Symbols);
        Assert.Equal("RelayCoil", symbol.Definition.Name);
        Assert.Equal("Coils", symbol.Definition.Category);
        Assert.Equal(2, symbol.Definition.ConnectionPoints.Count);
        Assert.Equal("A1", symbol.Definition.ConnectionPoints[0].Pin);
        Assert.Single(symbol.Definition.TextPlaceholders);
        Assert.Single(symbol.Definition.Variants);
        Assert.NotEmpty(symbol.SvgBytes);
    }

    [Fact]
    public void LoadFromFolder_JsonWithoutMatchingSvg_WarnsAndSkipsThatSymbol()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "RelayCoil.symbol.json"), ValidJson);
        // RelayCoil.svg deliberately not written.

        var result = SymbolLibraryLoader.LoadFromFolder(dir.Path);

        Assert.Empty(result.Symbols);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void LoadFromFolder_OneMalformedJson_WarnsButStillLoadsOthers()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "Broken.symbol.json"), "{ not valid json");
        File.WriteAllText(Path.Combine(dir.Path, "RelayCoil.symbol.json"), ValidJson);
        File.WriteAllText(Path.Combine(dir.Path, "RelayCoil.svg"), ValidSvg);

        var result = SymbolLibraryLoader.LoadFromFolder(dir.Path);

        Assert.Single(result.Warnings);
        var symbol = Assert.Single(result.Symbols);
        Assert.Equal("RelayCoil", symbol.Definition.Name);
    }

    [Fact]
    public void LoadFromFolder_MissingFolder_ReturnsEmptyWithWarning()
    {
        var result = SymbolLibraryLoader.LoadFromFolder(Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}"));

        Assert.Empty(result.Symbols);
        Assert.Single(result.Warnings);
    }
}
