using System.Text;
using Ecad.Rendering.Symbols;
using SkiaSharp;
using Xunit;

namespace Ecad.Rendering.Tests;

public class SymbolRasterizerTests
{
    [Fact]
    public void RasterizeToPng_SimpleSvg_ProducesPngOfRequestedSize()
    {
        var svg = """<svg xmlns="http://www.w3.org/2000/svg" width="40" height="40" viewBox="0 0 40 40"><rect x="5" y="5" width="30" height="30" fill="red"/></svg>""";
        var svgBytes = Encoding.UTF8.GetBytes(svg);

        var pngBytes = SymbolRasterizer.RasterizeToPng(svgBytes, 64, 96);

        Assert.NotEmpty(pngBytes);
        using var bitmap = SKBitmap.Decode(pngBytes);
        Assert.Equal(64, bitmap.Width);
        Assert.Equal(96, bitmap.Height);
    }

    [Fact]
    public void RasterizeToPng_InvalidSvg_Throws()
    {
        var notSvg = Encoding.UTF8.GetBytes("this is not an svg file");

        Assert.ThrowsAny<Exception>(() => SymbolRasterizer.RasterizeToPng(notSvg, 32, 32));
    }
}
