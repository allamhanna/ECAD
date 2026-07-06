using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ecad.App.Canvas;
using Ecad.App.Views;
using Ecad.Core.Models;
using Ecad.Data;
using Ecad.Rendering.Canvas;
using Ecad.Rendering.Symbols;
using SkiaSharp;
using Svg.Skia;

namespace Ecad.App.ViewModels;

/// <summary>A palette entry: the loaded symbol plus a small rasterized thumbnail for the sidebar.</summary>
public sealed record PaletteItem(LoadedSymbol Symbol, BitmapImage Thumbnail);

/// <summary>
/// Owns a schematic page's viewport, placements, selection and undo/redo stack, and translates
/// raw mouse/keyboard input (reported by SchematicPageWindow's code-behind) into IUndoableCommand
/// executions against the ProjectSession. Placement/Move/Rotate/Delete are all undoable; loading a
/// page's existing placements on open is not (there's nothing to undo about opening a page).
/// </summary>
public partial class SchematicPageViewModel : ObservableObject, IDisposable
{
    private readonly ProjectSession _session;
    private readonly Page _page;
    private readonly UndoRedoStack _undoRedo = new();
    private readonly Dictionary<string, LoadedSymbol> _symbolsByName;

    // Keyed by symbol name, kept alive for the ViewModel's lifetime: SKSvg.Dispose() also frees its
    // Picture's native memory, so disposing the SKSvg right after loading (as SymbolRasterizer does
    // for one-shot thumbnails) leaves any cached Picture pointing at freed memory — the AccessViolation
    // this caused, on the very next repaint after placing a symbol, is why this cache holds the SKSvg
    // itself rather than just its Picture.
    private readonly Dictionary<string, SKSvg> _svgCache = new();

    private long? _dragPlacementId;
    private (double X, double Y) _dragStartWorld;
    private (double X, double Y) _dragOriginalPosition;
    private bool _isPanning;
    private (double X, double Y) _panStartScreen;
    private (double PanX, double PanY) _panStartOffset;

    private const int PaletteThumbnailSize = 48;

    public CanvasViewport Viewport { get; } = new();
    public ObservableCollection<PlacementViewItem> Placements { get; } = [];
    public IReadOnlyList<PaletteItem> PaletteItems { get; }

    /// <summary>Set by SchematicPageWindow's code-behind after construction, so dialogs (DeviceTagDialog) can center on it.</summary>
    public Window? OwnerWindow { get; set; }

    [ObservableProperty]
    private LoadedSymbol? _selectedPaletteSymbol;

    [ObservableProperty]
    private long? _selectedPlacementId;

    [ObservableProperty]
    private string _statusText = "Select a symbol from the palette to place it, or click a placement to select it.";

    /// <summary>Raised whenever the canvas needs repainting — SchematicPageWindow subscribes and calls SKElement.InvalidateVisual().</summary>
    public event Action? RedrawRequested;

    public SchematicPageViewModel(ProjectSession session, Page page)
    {
        _session = session;
        _page = page;

        var folder = Path.Combine(AppContext.BaseDirectory, "SymbolLibrary");
        var result = SymbolLibraryLoader.LoadFromFolder(folder);
        _symbolsByName = result.Symbols.ToDictionary(s => s.Definition.Name);
        PaletteItems = result.Symbols
            .OrderBy(s => s.Definition.Category).ThenBy(s => s.Definition.Name)
            .Select(s => new PaletteItem(s, ToBitmapImage(SymbolRasterizer.RasterizeToPng(s.SvgBytes, PaletteThumbnailSize, PaletteThumbnailSize))))
            .ToList();

        foreach (var placement in _session.GetPlacements(_page.Id))
        {
            var symbol = _symbolsByName[placement.SymbolName];
            Placements.Add(new PlacementViewItem
            {
                PlacementId = placement.PlacementId,
                DeviceId = placement.DeviceId,
                DeviceTag = placement.DeviceTag,
                SymbolName = placement.SymbolName,
                Picture = GetOrLoadPicture(symbol),
                X = placement.X,
                Y = placement.Y,
                RotationDegrees = placement.RotationDegrees,
                Mirrored = placement.Mirrored,
            });
        }
    }

    [RelayCommand]
    private void SelectPaletteSymbol(LoadedSymbol symbol)
    {
        SelectedPaletteSymbol = symbol;
        SelectedPlacementId = null;
        StatusText = $"Click on the canvas to place '{symbol.Definition.Name}'.";
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        _undoRedo.Undo();
        NotifyUndoRedoChanged();
        RedrawRequested?.Invoke();
    }

    private bool CanUndo() => _undoRedo.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        _undoRedo.Redo();
        NotifyUndoRedoChanged();
        RedrawRequested?.Invoke();
    }

    private bool CanRedo() => _undoRedo.CanRedo;

    private void NotifyUndoRedoChanged()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    public void HandleLeftButtonDown(double screenX, double screenY)
    {
        if (SelectedPaletteSymbol is { } symbol)
        {
            PlaceSymbolAt(symbol, screenX, screenY);
            return;
        }

        var hit = PlacementHitTester.HitTest(BuildHitTestList(), Viewport, screenX, screenY);
        SelectedPlacementId = hit;

        if (hit is { } placementId)
        {
            var item = Placements.First(p => p.PlacementId == placementId);
            _dragPlacementId = placementId;
            _dragStartWorld = Viewport.ScreenToWorld(screenX, screenY);
            _dragOriginalPosition = (item.X, item.Y);
            StatusText = $"Selected {item.DeviceTag}.";
        }
        else
        {
            StatusText = "Nothing selected.";
        }

        RedrawRequested?.Invoke();
    }

    private void PlaceSymbolAt(LoadedSymbol symbol, double screenX, double screenY)
    {
        var (worldXRaw, worldYRaw) = Viewport.ScreenToWorld(screenX, screenY);
        var (worldX, worldY) = Viewport.SnapToGrid(worldXRaw, worldYRaw);

        SelectedPaletteSymbol = null;

        var dialog = new DeviceTagDialog { Owner = OwnerWindow };
        if (dialog.ShowDialog() != true)
        {
            StatusText = "Placement cancelled.";
            return;
        }

        var pinNames = symbol.Definition.ConnectionPoints.Count > 0
            ? symbol.Definition.ConnectionPoints.Select(cp => cp.Pin).ToList()
            : ["1"];

        _undoRedo.Execute(new PlaceSymbolCommand(_session, this, _page.Id, symbol, pinNames, worldX, worldY, dialog.DeviceTag));
        NotifyUndoRedoChanged();
        StatusText = $"Placed '{symbol.Definition.Name}' as {dialog.DeviceTag}.";
        RedrawRequested?.Invoke();
    }

    /// <summary>Double-click on a placement renames its device tag. Editing the symbol's geometry
    /// itself is the separate symbol editor (deferred to Phase 2, out of scope here).</summary>
    public void HandleDoubleClick(double screenX, double screenY)
    {
        var hit = PlacementHitTester.HitTest(BuildHitTestList(), Viewport, screenX, screenY);
        if (hit is not { } placementId) return;

        var item = Placements.First(p => p.PlacementId == placementId);
        var dialog = new DeviceTagDialog(item.DeviceTag) { Owner = OwnerWindow };
        if (dialog.ShowDialog() != true) return;

        if (dialog.DeviceTag == item.DeviceTag) return;

        _undoRedo.Execute(new RenameTagCommand(_session, this, placementId, item.DeviceId, item.DeviceTag, dialog.DeviceTag));
        NotifyUndoRedoChanged();
        StatusText = $"Renamed to {dialog.DeviceTag}.";
        RedrawRequested?.Invoke();
    }

    public void HandleRightButtonDown(double screenX, double screenY)
    {
        _isPanning = true;
        _panStartScreen = (screenX, screenY);
        _panStartOffset = (Viewport.PanX, Viewport.PanY);
    }

    public void HandleMouseMove(double screenX, double screenY)
    {
        if (_isPanning)
        {
            Viewport.PanX = _panStartOffset.PanX + (screenX - _panStartScreen.X) / Viewport.Zoom;
            Viewport.PanY = _panStartOffset.PanY + (screenY - _panStartScreen.Y) / Viewport.Zoom;
            RedrawRequested?.Invoke();
            return;
        }

        if (_dragPlacementId is { } placementId)
        {
            var (worldX, worldY) = Viewport.ScreenToWorld(screenX, screenY);
            var (snappedX, snappedY) = Viewport.SnapToGrid(
                _dragOriginalPosition.X + (worldX - _dragStartWorld.X),
                _dragOriginalPosition.Y + (worldY - _dragStartWorld.Y));

            var item = Placements.First(p => p.PlacementId == placementId);
            item.X = snappedX;
            item.Y = snappedY;
            RedrawRequested?.Invoke();
        }
    }

    public void HandleLeftButtonUp()
    {
        if (_dragPlacementId is not { } placementId) return;
        _dragPlacementId = null;

        var item = Placements.First(p => p.PlacementId == placementId);
        var finalX = item.X;
        var finalY = item.Y;

        if (finalX == _dragOriginalPosition.X && finalY == _dragOriginalPosition.Y) return;

        // The drag already moved the view item live for visual feedback — put it back to the start
        // so MoveCommand.Do() is the single place that advances it, keeping undo/redo consistent.
        item.X = _dragOriginalPosition.X;
        item.Y = _dragOriginalPosition.Y;
        _undoRedo.Execute(new MoveCommand(_session, this, placementId, _dragOriginalPosition.X, _dragOriginalPosition.Y, finalX, finalY));
        NotifyUndoRedoChanged();
    }

    public void HandleRightButtonUp() => _isPanning = false;

    public void HandleMouseWheel(double screenX, double screenY, int delta)
    {
        var (worldX, worldY) = Viewport.ScreenToWorld(screenX, screenY);
        var factor = delta > 0 ? 1.1 : 1 / 1.1;
        Viewport.Zoom = Math.Clamp(Viewport.Zoom * factor, 0.1, 8.0);

        // Keep the point under the cursor stable while zooming.
        var (newScreenX, newScreenY) = Viewport.WorldToScreen(worldX, worldY);
        Viewport.PanX += (screenX - newScreenX) / Viewport.Zoom;
        Viewport.PanY += (screenY - newScreenY) / Viewport.Zoom;

        RedrawRequested?.Invoke();
    }

    public void HandleKeyDown(Key key, bool ctrlPressed)
    {
        if (ctrlPressed && key == Key.Z)
        {
            if (UndoCommand.CanExecute(null)) UndoCommand.Execute(null);
            return;
        }

        if (ctrlPressed && key == Key.Y)
        {
            if (RedoCommand.CanExecute(null)) RedoCommand.Execute(null);
            return;
        }

        if (key == Key.Delete && SelectedPlacementId is { } deleteId)
        {
            var item = Placements.First(p => p.PlacementId == deleteId);
            var symbol = _symbolsByName[item.SymbolName];
            var pinNames = _session.GetDevicePins(item.DeviceId).Select(p => p.Name).ToList();
            _undoRedo.Execute(new DeleteCommand(_session, this, _page.Id, item, symbol, pinNames));
            NotifyUndoRedoChanged();
            SelectedPlacementId = null;
            RedrawRequested?.Invoke();
            return;
        }

        if (key == Key.R && SelectedPlacementId is { } rotateId)
        {
            var item = Placements.First(p => p.PlacementId == rotateId);
            var newRotation = (item.RotationDegrees + 90) % 360;
            _undoRedo.Execute(new RotateCommand(_session, this, rotateId, item.RotationDegrees, item.Mirrored, newRotation, item.Mirrored));
            NotifyUndoRedoChanged();
            RedrawRequested?.Invoke();
        }
    }

    public IReadOnlyList<PlacementRenderInfo> BuildRenderList() =>
        Placements.Select(p => new PlacementRenderInfo(p.PlacementId, p.DeviceTag, p.X, p.Y,
            PlacementViewItem.Width, PlacementViewItem.Height, p.RotationDegrees, p.Mirrored, p.Picture)).ToList();

    private List<HitTestPlacement> BuildHitTestList() =>
        Placements.Select(p => new HitTestPlacement(p.PlacementId, p.X, p.Y,
            PlacementViewItem.Width, PlacementViewItem.Height, p.RotationDegrees)).ToList();

    internal void AddPlacementToView(long placementId, long deviceId, string deviceTag, LoadedSymbol symbol,
        double x, double y, int rotationDegrees, bool mirrored)
    {
        Placements.Add(new PlacementViewItem
        {
            PlacementId = placementId,
            DeviceId = deviceId,
            DeviceTag = deviceTag,
            SymbolName = symbol.Definition.Name,
            Picture = GetOrLoadPicture(symbol),
            X = x,
            Y = y,
            RotationDegrees = rotationDegrees,
            Mirrored = mirrored,
        });
    }

    internal void RemovePlacementFromView(long placementId)
    {
        var item = Placements.FirstOrDefault(p => p.PlacementId == placementId);
        if (item is not null) Placements.Remove(item);
    }

    internal void UpdatePlacementPosition(long placementId, double x, double y)
    {
        var item = Placements.First(p => p.PlacementId == placementId);
        item.X = x;
        item.Y = y;
    }

    internal void UpdatePlacementRotation(long placementId, int rotationDegrees, bool mirrored)
    {
        var item = Placements.First(p => p.PlacementId == placementId);
        item.RotationDegrees = rotationDegrees;
        item.Mirrored = mirrored;
    }

    internal void UpdatePlacementTag(long placementId, string deviceTag)
    {
        var item = Placements.First(p => p.PlacementId == placementId);
        item.DeviceTag = deviceTag;
    }

    private SKPicture GetOrLoadPicture(LoadedSymbol symbol)
    {
        if (_svgCache.TryGetValue(symbol.Definition.Name, out var cachedSvg)) return cachedSvg.Picture!;

        var svg = new SKSvg();
        using var stream = new MemoryStream(symbol.SvgBytes);
        var picture = svg.Load(stream) ?? throw new InvalidOperationException($"Failed to parse SVG for '{symbol.Definition.Name}'.");
        _svgCache[symbol.Definition.Name] = svg;
        return picture;
    }

    public void Dispose()
    {
        foreach (var svg in _svgCache.Values) svg.Dispose();
        _svgCache.Clear();
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
