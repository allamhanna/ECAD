using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ecad.Rendering.Symbols;

namespace Ecad.App.ViewModels;

/// <summary>A loaded symbol plus its pre-rasterized thumbnail, for display in the Symbol Browser.</summary>
public sealed record SymbolBrowserItem(SymbolDefinition Definition, BitmapImage Thumbnail);

/// <summary>
/// Loads the bundled starter symbol library (see DECISIONS.md ADR-006) and rasterizes each one to
/// a thumbnail. Read-only browse for M4 — placing symbols on a page is M5.
/// </summary>
public partial class SymbolBrowserViewModel : ObservableObject
{
    private const int ThumbnailSize = 96;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public ObservableCollection<SymbolBrowserItem> Symbols { get; } = [];

    public SymbolBrowserViewModel()
    {
        Load();
    }

    [RelayCommand]
    private void Refresh() => Load();

    private void Load()
    {
        Symbols.Clear();

        var folder = Path.Combine(AppContext.BaseDirectory, "SymbolLibrary");
        var result = SymbolLibraryLoader.LoadFromFolder(folder);
        var warnings = new List<string>(result.Warnings);

        foreach (var symbol in result.Symbols.OrderBy(s => s.Definition.Category).ThenBy(s => s.Definition.Name))
        {
            try
            {
                var pngBytes = SymbolRasterizer.RasterizeToPng(symbol.SvgBytes, ThumbnailSize, ThumbnailSize);
                Symbols.Add(new SymbolBrowserItem(symbol.Definition, ToBitmapImage(pngBytes)));
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to rasterize '{symbol.Definition.Name}': {ex.Message}");
            }
        }

        StatusText = warnings.Count == 0
            ? $"{Symbols.Count} symbols loaded."
            : $"{Symbols.Count} symbols loaded, {warnings.Count} warning(s): {string.Join(" | ", warnings)}";
    }

    private static BitmapImage ToBitmapImage(byte[] pngBytes)
    {
        var image = new BitmapImage();
        using var stream = new MemoryStream(pngBytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
