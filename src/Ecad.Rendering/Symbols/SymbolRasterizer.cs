using System.IO;
using SkiaSharp;
using Svg.Skia;

namespace Ecad.Rendering.Symbols;

/// <summary>
/// Rasterizes a symbol's SVG to a PNG byte array at a fixed pixel size, for static thumbnail
/// display (the Symbol Browser, M4). No WPF dependency — plain SkiaSharp + Svg.Skia, so this is
/// unit-testable on its own; the interactive live-canvas rendering (SkiaSharp.Views.WPF) is M5.
/// </summary>
public static class SymbolRasterizer
{
    public static byte[] RasterizeToPng(byte[] svgBytes, int widthPixels, int heightPixels)
    {
        using var svg = new SKSvg();
        using var stream = new MemoryStream(svgBytes);
        var picture = svg.Load(stream) ?? throw new InvalidOperationException("Failed to parse SVG.");

        using var bitmap = new SKBitmap(widthPixels, heightPixels);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            if (picture.CullRect.Width > 0 && picture.CullRect.Height > 0)
            {
                canvas.Scale(widthPixels / picture.CullRect.Width, heightPixels / picture.CullRect.Height);
            }
            canvas.DrawPicture(picture);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
