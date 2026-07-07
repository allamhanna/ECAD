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

    // Wire-drawing drag state (M7) — parallel to the placement-drag/pan state above, not replacing it.
    private long? _wireDrawFromDevicePinId;
    private WorldPoint _wireDrawCurrentWorld;

    private const int PaletteThumbnailSize = 48;
    private const double PinHitTolerance = 6; // world units — half a grid cell
    private const double WireHitTolerance = 4;
    private const double PinSnapRadius = 12; // world units — magnetic pull toward a nearby pin while placing/dragging

    public CanvasViewport Viewport { get; } = new();
    public ObservableCollection<PlacementViewItem> Placements { get; } = [];
    public ObservableCollection<ConnectionViewItem> Connections { get; } = [];
    public IReadOnlyList<PaletteItem> PaletteItems { get; }

    /// <summary>Set by SchematicPageWindow's code-behind after construction, so dialogs (DeviceTagDialog) can center on it.</summary>
    public Window? OwnerWindow { get; set; }

    [ObservableProperty]
    private LoadedSymbol? _selectedPaletteSymbol;

    [ObservableProperty]
    private long? _selectedPlacementId;

    [ObservableProperty]
    private long? _selectedConnectionId;

    [ObservableProperty]
    private string _statusText = "Select a symbol from the palette to place it, click a pin to draw a wire, or click a placement to select it.";

    /// <summary>Raised whenever the canvas needs repainting — SchematicPageWindow subscribes and calls SKElement.InvalidateVisual().</summary>
    public event Action? RedrawRequested;

    /// <summary>Raised when Ctrl+Click hits a placement that has a sibling elsewhere (Section 5.4
    /// cross-reference — most notably an interruption-point pair) — carries the sibling's PageId and
    /// PlacementId. SchematicPageWindow handles this by opening that page with the placement selected.</summary>
    public event Action<long, long>? NavigateToPageRequested;

    public SchematicPageViewModel(ProjectSession session, Page page, long? focusPlacementId = null)
    {
        _session = session;
        _page = page;
        _session.PlacementsChanged += OnSessionPlacementsChanged;
        _session.ConnectionsChanged += OnSessionConnectionsChanged;

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
                Function = placement.FunctionSegment,
                Location = placement.LocationSegment,
                SymbolName = placement.SymbolName,
                Picture = GetOrLoadPicture(symbol),
                X = placement.X,
                Y = placement.Y,
                RotationDegrees = placement.RotationDegrees,
                Mirrored = placement.Mirrored,
                SiblingPageLabels = placement.Siblings.Select(s => s.PageLabel).ToList(),
                Siblings = placement.Siblings,
                Pins = placement.Pins,
            });
        }

        foreach (var connection in _session.GetConnectionsForPage(_page.Id))
        {
            Connections.Add(new ConnectionViewItem
            {
                ConnectionId = connection.Id,
                FromDevicePinId = connection.FromDevicePinId,
                ToDevicePinId = connection.ToDevicePinId,
                WireNumber = connection.WireNumber,
            });
        }

        if (focusPlacementId is { } id && Placements.Any(p => p.PlacementId == id))
            SelectedPlacementId = id;
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
        RefreshFromSession();
    }

    private bool CanUndo() => _undoRedo.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        _undoRedo.Redo();
        NotifyUndoRedoChanged();
        RefreshFromSession();
    }

    private bool CanRedo() => _undoRedo.CanRedo;

    private void NotifyUndoRedoChanged()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    public void HandleLeftButtonDown(double screenX, double screenY, bool ctrlPressed = false)
    {
        if (SelectedPaletteSymbol is { } symbol)
        {
            PlaceSymbolAt(symbol, screenX, screenY);
            return;
        }

        var (worldX, worldY) = Viewport.ScreenToWorld(screenX, screenY);
        var worldPoint = new WorldPoint(worldX, worldY);
        var pinPositions = BuildPinPositions();

        // Pins are the most specific target — a click near one starts a wire instead of selecting the placement it sits on.
        if (!ctrlPressed && WireHitTester.HitTestPin(worldPoint, pinPositions, PinHitTolerance) is { } fromPinId)
        {
            _wireDrawFromDevicePinId = fromPinId;
            _wireDrawCurrentWorld = worldPoint;
            SelectedPlacementId = null;
            SelectedConnectionId = null;
            StatusText = "Drag to another pin to draw a wire.";
            RedrawRequested?.Invoke();
            return;
        }

        var placementHit = PlacementHitTester.HitTest(BuildHitTestList(), Viewport, screenX, screenY);
        if (placementHit is { } placementId)
        {
            var item = Placements.First(p => p.PlacementId == placementId);

            if (ctrlPressed && item.Siblings.Count > 0)
            {
                var sibling = item.Siblings[0];
                NavigateToPageRequested?.Invoke(sibling.PageId, sibling.PlacementId);
                return;
            }

            SelectedPlacementId = placementId;
            SelectedConnectionId = null;
            _dragPlacementId = placementId;
            _dragStartWorld = (worldPoint.X, worldPoint.Y);
            _dragOriginalPosition = (item.X, item.Y);
            StatusText = $"Selected {item.DeviceTag}.";
            RedrawRequested?.Invoke();
            return;
        }

        var pinPositionsById = pinPositions.ToDictionary(p => p.DevicePinId, p => p.Position);
        var wireHit = WireHitTester.HitTestWire(worldPoint, BuildWireHitTestList(pinPositionsById), WireHitTolerance);
        SelectedPlacementId = null;
        SelectedConnectionId = wireHit;
        StatusText = wireHit is not null ? "Selected wire." : "Nothing selected.";
        RedrawRequested?.Invoke();
    }

    private void PlaceSymbolAt(LoadedSymbol symbol, double screenX, double screenY)
    {
        var (worldXRaw, worldYRaw) = Viewport.ScreenToWorld(screenX, screenY);
        var (worldX, worldY) = Viewport.SnapToGrid(worldXRaw, worldYRaw);

        // Magnetic pin snap: auto-connect needs a facing pin's row/column to line up exactly, which
        // is nearly impossible to land by eye (pin offsets aren't visible until after placing). Nudges
        // the placement so a nearby compatible pin's line becomes an exact match, the same way most
        // ECAD tools snap wires/pins together.
        var localPins = symbol.Definition.ConnectionPoints.Select(cp => (cp.X, cp.Y, cp.Direction));
        (worldX, worldY) = ApplyPinMagnetSnap(localPins, 0, false, worldX, worldY, BuildPinPositions());

        SelectedPaletteSymbol = null;

        var existingDevices = _session.GetAllDevices();
        var suggestedDesignation = _session.SuggestNextDesignation(_page.FunctionSegment, _page.LocationSegment);
        var dialog = new DeviceTagDialog(existingDevices, _page.FunctionSegment, _page.LocationSegment, suggestedDesignation, IsTagAvailable)
        {
            Owner = OwnerWindow,
        };
        if (dialog.ShowDialog() != true)
        {
            StatusText = "Placement cancelled.";
            return;
        }

        var pinNames = symbol.Definition.ConnectionPoints.Count > 0
            ? symbol.Definition.ConnectionPoints.Select(cp => cp.Pin).ToList()
            : ["1"];

        long placedId;
        if (dialog.SelectedExistingDeviceId is { } existingDeviceId)
        {
            var existingDevice = existingDevices.First(d => d.Id == existingDeviceId);
            var command = new AttachPlacementCommand(_session, this, _page.Id, existingDeviceId,
                existingDevice.FunctionSegment, existingDevice.LocationSegment, existingDevice.DeviceTagSegment, symbol, pinNames, worldX, worldY);
            _undoRedo.Execute(command);
            placedId = command.PlacementId;
            StatusText = $"Placed '{symbol.Definition.Name}' on existing device {existingDevice.DeviceTagSegment}.";
        }
        else
        {
            var command = new PlaceSymbolCommand(_session, this, _page.Id, symbol, pinNames, worldX, worldY,
                dialog.Function, dialog.Location, dialog.Designation);
            _undoRedo.Execute(command);
            placedId = command.PlacementId;
            StatusText = $"Placed '{symbol.Definition.Name}' as {dialog.Designation}.";
        }

        RunAutoConnect(placedId);
        NotifyUndoRedoChanged();
        RefreshFromSession();
    }

    private bool IsTagAvailable(string? function, string? location, string designation, long? excludingDeviceId) =>
        _session.IsTagAvailable(function, location, designation, excludingDeviceId);

    /// <summary>Double-click on a placement renames its device tag; double-click on a wire renames its
    /// wire number. Editing the symbol's geometry itself is the separate symbol editor (deferred to
    /// Phase 2, out of scope here).</summary>
    public void HandleDoubleClick(double screenX, double screenY)
    {
        var placementHit = PlacementHitTester.HitTest(BuildHitTestList(), Viewport, screenX, screenY);
        if (placementHit is { } placementId)
        {
            var item = Placements.First(p => p.PlacementId == placementId);
            var device = _session.GetDevice(item.DeviceId)!;
            var dialog = new DeviceTagDialog(device, IsTagAvailable) { Owner = OwnerWindow };
            if (dialog.ShowDialog() != true) return;

            if (dialog.Function == item.Function && dialog.Location == item.Location && dialog.Designation == item.DeviceTag) return;

            _undoRedo.Execute(new RenameTagCommand(_session, this, placementId, item.DeviceId,
                item.Function, item.Location, item.DeviceTag, dialog.Function, dialog.Location, dialog.Designation));
            NotifyUndoRedoChanged();
            StatusText = $"Renamed to {dialog.Designation}.";
            RedrawRequested?.Invoke();
            return;
        }

        var (worldX, worldY) = Viewport.ScreenToWorld(screenX, screenY);
        var pinPositions = BuildPinPositions();
        var pinPositionsById = pinPositions.ToDictionary(p => p.DevicePinId, p => p.Position);
        var wireHit = WireHitTester.HitTestWire(new WorldPoint(worldX, worldY), BuildWireHitTestList(pinPositionsById), WireHitTolerance);
        if (wireHit is not { } connectionId) return;

        var connectionItem = Connections.First(c => c.ConnectionId == connectionId);
        var wireDialog = new WireNumberDialog(connectionItem.WireNumber ?? string.Empty,
            n => _session.IsWireNumberAvailable(n, connectionId)) { Owner = OwnerWindow };
        if (wireDialog.ShowDialog() != true) return;
        if (wireDialog.WireNumber == connectionItem.WireNumber) return;

        _undoRedo.Execute(new RenameWireNumberCommand(_session, this, connectionId, connectionItem.WireNumber ?? string.Empty, wireDialog.WireNumber));
        NotifyUndoRedoChanged();
        StatusText = $"Renamed wire to {wireDialog.WireNumber}.";
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

        if (_wireDrawFromDevicePinId is not null)
        {
            var (worldX, worldY) = Viewport.ScreenToWorld(screenX, screenY);
            var (snappedX, snappedY) = Viewport.SnapToGrid(worldX, worldY);
            _wireDrawCurrentWorld = new WorldPoint(snappedX, snappedY);
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
            var draggedPinIds = item.Pins.Select(p => p.DevicePinId).ToHashSet();
            var otherPins = BuildPinPositions().Where(p => !draggedPinIds.Contains(p.DevicePinId)).ToList();
            var symbol = _symbolsByName[item.SymbolName];
            var localPins = item.Pins
                .Select(pin => symbol.Definition.ConnectionPoints.FirstOrDefault(cp => cp.Pin == pin.Name))
                .Where(cp => cp is not null)
                .Select(cp => (cp!.X, cp.Y, cp.Direction));
            (snappedX, snappedY) = ApplyPinMagnetSnap(localPins, item.RotationDegrees, item.Mirrored, snappedX, snappedY, otherPins);

            item.X = snappedX;
            item.Y = snappedY;
            RedrawRequested?.Invoke();
        }
    }

    public void HandleLeftButtonUp()
    {
        if (_wireDrawFromDevicePinId is { } fromPinId)
        {
            var toPinId = WireHitTester.HitTestPin(_wireDrawCurrentWorld, BuildPinPositions(), PinHitTolerance);
            _wireDrawFromDevicePinId = null;

            if (toPinId is { } targetPinId && targetPinId != fromPinId && !_session.AreConnected(fromPinId, targetPinId))
            {
                _undoRedo.Execute(new CreateConnectionCommand(_session, this, fromPinId, targetPinId));
                NotifyUndoRedoChanged();
                StatusText = "Wire created.";
            }
            else
            {
                StatusText = "Wire cancelled.";
            }
            RedrawRequested?.Invoke();
            return;
        }

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
        RunAutoConnect(placementId);
        NotifyUndoRedoChanged();
        RedrawRequested?.Invoke();
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

        if (key == Key.Delete && SelectedConnectionId is { } deleteConnectionId)
        {
            var connectionItem = Connections.First(c => c.ConnectionId == deleteConnectionId);
            _undoRedo.Execute(new DeleteConnectionCommand(_session, this, connectionItem));
            NotifyUndoRedoChanged();
            SelectedConnectionId = null;
            RedrawRequested?.Invoke();
            return;
        }

        if (key == Key.Delete && SelectedPlacementId is { } deleteId)
        {
            var item = Placements.First(p => p.PlacementId == deleteId);
            var symbol = _symbolsByName[item.SymbolName];
            _undoRedo.Execute(new DeleteCommand(_session, this, _page.Id, item, symbol));
            NotifyUndoRedoChanged();
            SelectedPlacementId = null;
            RefreshFromSession();
            return;
        }

        if (key == Key.R && SelectedPlacementId is { } rotateId)
        {
            var item = Placements.First(p => p.PlacementId == rotateId);
            var newRotation = (item.RotationDegrees + 90) % 360;
            _undoRedo.Execute(new RotateCommand(_session, this, rotateId, item.RotationDegrees, item.Mirrored, newRotation, item.Mirrored));
            RunAutoConnect(rotateId);
            NotifyUndoRedoChanged();
            RedrawRequested?.Invoke();
        }
    }

    [RelayCommand]
    private void RenumberWires()
    {
        _undoRedo.Execute(new RenumberWiresCommand(_session, this));
        NotifyUndoRedoChanged();
        StatusText = "Wires renumbered.";
    }

    public IReadOnlyList<PlacementRenderInfo> BuildRenderList() =>
        Placements.Select(p => new PlacementRenderInfo(p.PlacementId, p.DeviceTag, p.X, p.Y,
            PlacementViewItem.Width, PlacementViewItem.Height, p.RotationDegrees, p.Mirrored, p.Picture, p.SiblingPageLabels)).ToList();

    /// <summary>Everything M7 adds to the render pass — pins, wires, junctions, and the wire-draw preview if one's in progress.</summary>
    public WiringRenderInfo BuildWiringRenderInfo()
    {
        var pinPositions = BuildPinPositions();
        var pinPositionsById = pinPositions.ToDictionary(p => p.DevicePinId, p => p.Position);

        // Whether a drag ends up connected or not is only decided on drop (RunAutoConnect) — stretching
        // an existing wire live to follow the cursor mid-drag would visually imply it's continuously
        // "attached," which it isn't. Hide wires touching the dragged placement's pins for the
        // duration of the drag; they reappear (connected, reconnected, or gone) the moment it's released.
        var draggedPinIds = _dragPlacementId is { } draggedPlacementId
            ? Placements.FirstOrDefault(p => p.PlacementId == draggedPlacementId)?.Pins.Select(p => p.DevicePinId).ToHashSet() ?? []
            : [];

        var existingConnections = new List<ExistingConnection>();
        var wireRenderInfos = new List<WireRenderInfo>();
        foreach (var connection in Connections)
        {
            if (draggedPinIds.Contains(connection.FromDevicePinId) || draggedPinIds.Contains(connection.ToDevicePinId)) continue;
            if (!pinPositionsById.TryGetValue(connection.FromDevicePinId, out var from)) continue;
            if (!pinPositionsById.TryGetValue(connection.ToDevicePinId, out var to)) continue;

            var route = OrthogonalRouter.Route(from, to);
            existingConnections.Add(new ExistingConnection(connection.ConnectionId, connection.FromDevicePinId, connection.ToDevicePinId, route));
            wireRenderInfos.Add(new WireRenderInfo(connection.ConnectionId, connection.WireNumber, route));
        }

        var junctions = JunctionDetector.FindJunctions(existingConnections, pinPositions);

        IReadOnlyList<WorldPoint>? previewRoute = null;
        if (_wireDrawFromDevicePinId is { } fromPinId && pinPositionsById.TryGetValue(fromPinId, out var fromPos))
            previewRoute = OrthogonalRouter.Route(fromPos, _wireDrawCurrentWorld);

        return new WiringRenderInfo(
            pinPositions.Select(p => new PinRenderInfo(p.DevicePinId, p.Position)).ToList(),
            wireRenderInfos, SelectedConnectionId, junctions, previewRoute);
    }

    private List<HitTestPlacement> BuildHitTestList() =>
        Placements.Select(p => new HitTestPlacement(p.PlacementId, p.X, p.Y,
            PlacementViewItem.Width, PlacementViewItem.Height, p.RotationDegrees)).ToList();

    /// <summary>Every pin's current world position, resolved via each placement's symbol connection points
    /// transformed by its current position/rotation/mirror — recomputed fresh every call so drag/rotate stay live.</summary>
    private List<PinPosition> BuildPinPositions()
    {
        var positions = new List<PinPosition>();
        foreach (var item in Placements)
        {
            var symbol = _symbolsByName[item.SymbolName];
            foreach (var pin in item.Pins)
            {
                var connectionPoint = symbol.Definition.ConnectionPoints.FirstOrDefault(cp => cp.Pin == pin.Name);
                if (connectionPoint is null) continue;

                var (worldX, worldY) = PlacementPinGeometry.GetPinWorldPosition(
                    item.X, item.Y, item.RotationDegrees, item.Mirrored, connectionPoint.X, connectionPoint.Y);
                var direction = PlacementPinGeometry.GetPinWorldDirection(item.RotationDegrees, item.Mirrored, connectionPoint.Direction);
                positions.Add(new PinPosition(pin.DevicePinId, new WorldPoint(worldX, worldY), direction));
            }
        }
        return positions;
    }

    /// <summary>
    /// Auto-connect (AutoConnectDetector) only requires two facing pins to share a grid line, not to
    /// touch — so the snap that makes this achievable by mouse pulls just the relevant axis into exact
    /// alignment: a left/right-facing pin's Y toward a compatible pin's Y, an up/down-facing pin's X
    /// toward a compatible pin's X. The other axis (how far apart they end up) is left wherever the
    /// user placed/dragged it. Only pins with opposite directions are considered — this never pulls a
    /// pin toward one it couldn't actually connect to.
    /// </summary>
    private static (double X, double Y) ApplyPinMagnetSnap(IEnumerable<(double X, double Y, double Direction)> localPins, int rotationDegrees, bool mirrored,
        double candidateX, double candidateY, IReadOnlyList<PinPosition> otherPins)
    {
        double? bestDistance = null;
        var snappedX = candidateX;
        var snappedY = candidateY;

        foreach (var (localX, localY, localDirection) in localPins)
        {
            var (candidatePinX, candidatePinY) = PlacementPinGeometry.GetPinWorldPosition(
                candidateX, candidateY, rotationDegrees, mirrored, localX, localY);
            var candidateDirection = PlacementPinGeometry.GetPinWorldDirection(rotationDegrees, mirrored, localDirection);
            var isHorizontalFacing = IsHorizontalFacing(candidateDirection);

            foreach (var otherPin in otherPins)
            {
                if (!AutoConnectDetector.AreOppositeDirections(candidateDirection, otherPin.Direction)) continue;

                var axisDelta = isHorizontalFacing ? otherPin.Position.Y - candidatePinY : otherPin.Position.X - candidatePinX;
                var distance = Math.Abs(axisDelta);
                if (distance > PinSnapRadius) continue;
                if (bestDistance is not null && distance >= bestDistance) continue;

                bestDistance = distance;
                snappedX = isHorizontalFacing ? candidateX : candidateX + axisDelta;
                snappedY = isHorizontalFacing ? candidateY + axisDelta : candidateY;
            }
        }

        return (snappedX, snappedY);
    }

    private static bool IsHorizontalFacing(double direction)
    {
        var normalized = ((direction % 360) + 360) % 360;
        return normalized is 0 or 180;
    }

    private List<HitTestWire> BuildWireHitTestList(IReadOnlyDictionary<long, WorldPoint> pinPositionsById)
    {
        var wires = new List<HitTestWire>();
        foreach (var connection in Connections)
        {
            if (!pinPositionsById.TryGetValue(connection.FromDevicePinId, out var from)) continue;
            if (!pinPositionsById.TryGetValue(connection.ToDevicePinId, out var to)) continue;
            wires.Add(new HitTestWire(connection.ConnectionId, OrthogonalRouter.Route(from, to)));
        }
        return wires;
    }

    /// <summary>
    /// Keeps a just-placed/moved/rotated placement's wiring consistent with its current geometry.
    /// A connection is only valid while its two pins are on the same grid line and facing each other
    /// (AutoConnectDetector.AreFacingEachOther) — this is re-checked every time, not just at creation
    /// time, so a wire does NOT simply stretch to follow a component wherever it goes: move a
    /// connected pin off that line and the connection is dropped, exactly like it never auto-connects
    /// to a non-facing pin in the first place. New facing touches are still auto-connected the same
    /// way. Only runs after interactive actions, not after undo/redo of them — a deliberate scope
    /// limit (ADR-009).
    /// </summary>
    private void RunAutoConnect(long placementId)
    {
        var item = Placements.FirstOrDefault(p => p.PlacementId == placementId);
        if (item is null || item.Pins.Count == 0) return;

        var allPinPositions = BuildPinPositions();
        var pinsById = allPinPositions.ToDictionary(p => p.DevicePinId);
        var movedPinIds = item.Pins.Select(p => p.DevicePinId).ToHashSet();
        var movedPins = allPinPositions.Where(p => movedPinIds.Contains(p.DevicePinId)).ToList();
        var otherPins = allPinPositions.Where(p => !movedPinIds.Contains(p.DevicePinId)).ToList();

        var brokenConnections = Connections
            .Where(c => movedPinIds.Contains(c.FromDevicePinId) || movedPinIds.Contains(c.ToDevicePinId))
            .Where(c => pinsById.ContainsKey(c.FromDevicePinId) && pinsById.ContainsKey(c.ToDevicePinId))
            .Where(c => !AutoConnectDetector.AreFacingEachOther(pinsById[c.FromDevicePinId], pinsById[c.ToDevicePinId]))
            .ToList();
        foreach (var connection in brokenConnections)
            _undoRedo.Execute(new DeleteConnectionCommand(_session, this, connection));

        var pinPositionsById = allPinPositions.ToDictionary(p => p.DevicePinId, p => p.Position);
        var existingConnections = Connections
            .Where(c => pinPositionsById.ContainsKey(c.FromDevicePinId) && pinPositionsById.ContainsKey(c.ToDevicePinId))
            .Select(c => new ExistingConnection(c.ConnectionId, c.FromDevicePinId, c.ToDevicePinId,
                OrthogonalRouter.Route(pinPositionsById[c.FromDevicePinId], pinPositionsById[c.ToDevicePinId])))
            .ToList();

        var newPairs = AutoConnectDetector.FindNewConnections(movedPins, otherPins, existingConnections, _session.AreConnected);
        foreach (var (fromId, toId) in newPairs)
            _undoRedo.Execute(new CreateConnectionCommand(_session, this, fromId, toId));

        if (brokenConnections.Count > 0 || newPairs.Count > 0)
            StatusText = $"{newPairs.Count} wire(s) auto-connected, {brokenConnections.Count} disconnected.";
    }

    /// <summary>
    /// Fired by ProjectSession.PlacementsChanged when placements are added/removed or a Device is
    /// renamed anywhere in the project — including from a different, simultaneously-open
    /// SchematicPageWindow for another page of the same multi-placement Device. Without this, this
    /// page's cross-reference text and tags would only ever reflect changes made in THIS window.
    /// </summary>
    private void OnSessionPlacementsChanged() => RefreshFromSession();

    /// <summary>Re-queries this page's placements from the DB and syncs each surviving view item's
    /// tag/segments/sibling labels (Section 5.4) — not position/rotation, which only ever change via
    /// this window's own Move/Rotate commands. Called after any local place/attach/delete/rename, on
    /// undo/redo, and whenever ProjectSession.PlacementsChanged fires from elsewhere.</summary>
    private void RefreshFromSession()
    {
        var fresh = _session.GetPlacements(_page.Id).ToDictionary(p => p.PlacementId, p => p);
        foreach (var item in Placements)
        {
            if (!fresh.TryGetValue(item.PlacementId, out var placement)) continue;
            item.DeviceTag = placement.DeviceTag;
            item.Function = placement.FunctionSegment;
            item.Location = placement.LocationSegment;
            item.SiblingPageLabels = placement.Siblings.Select(s => s.PageLabel).ToList();
            item.Siblings = placement.Siblings;
        }
        RedrawRequested?.Invoke();
    }

    internal void AddPlacementToView(long placementId, long deviceId, string? function, string? location, string deviceTag,
        LoadedSymbol symbol, double x, double y, int rotationDegrees, bool mirrored)
    {
        Placements.Add(new PlacementViewItem
        {
            PlacementId = placementId,
            DeviceId = deviceId,
            DeviceTag = deviceTag,
            Function = function,
            Location = location,
            SymbolName = symbol.Definition.Name,
            Picture = GetOrLoadPicture(symbol),
            X = x,
            Y = y,
            RotationDegrees = rotationDegrees,
            Mirrored = mirrored,
            Pins = _session.GetPlacementPins(placementId),
        });
    }

    internal void RemovePlacementFromView(long placementId)
    {
        var item = Placements.FirstOrDefault(p => p.PlacementId == placementId);
        if (item is not null) Placements.Remove(item);
    }

    /// <summary>Selects a placement already on this page — used when an already-open window is
    /// brought to front by navigation instead of a new one being constructed with focusPlacementId.</summary>
    internal void FocusPlacement(long placementId)
    {
        if (!Placements.Any(p => p.PlacementId == placementId)) return;
        SelectedPlacementId = placementId;
        RedrawRequested?.Invoke();
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

    internal void UpdatePlacementTag(long placementId, string? function, string? location, string deviceTag)
    {
        var item = Placements.First(p => p.PlacementId == placementId);
        item.Function = function;
        item.Location = location;
        item.DeviceTag = deviceTag;
    }

    internal void AddConnectionToView(long connectionId, string? wireNumber, long fromDevicePinId, long toDevicePinId)
    {
        // Idempotent: ProjectSession.CreateConnection raises ConnectionsChanged synchronously (before
        // returning to the caller), which this same ViewModel handles by reloading all of this page's
        // connections from the DB — so by the time a command's Do() reaches this call, the connection
        // may already be present. Without this guard it would be added a second time.
        if (Connections.Any(c => c.ConnectionId == connectionId)) return;

        Connections.Add(new ConnectionViewItem
        {
            ConnectionId = connectionId,
            FromDevicePinId = fromDevicePinId,
            ToDevicePinId = toDevicePinId,
            WireNumber = wireNumber,
        });
    }

    internal void RemoveConnectionFromView(long connectionId)
    {
        var item = Connections.FirstOrDefault(c => c.ConnectionId == connectionId);
        if (item is not null) Connections.Remove(item);
    }

    internal void UpdateConnectionWireNumber(long connectionId, string wireNumber)
    {
        var item = Connections.First(c => c.ConnectionId == connectionId);
        item.WireNumber = wireNumber;
    }

    /// <summary>Fired by ProjectSession.ConnectionsChanged (M7's analog of OnSessionPlacementsChanged) —
    /// keeps this page's wires live across every other open SchematicPageWindow too.</summary>
    private void OnSessionConnectionsChanged() => ReloadConnectionsFromSession();

    internal void ReloadConnectionsFromSession()
    {
        Connections.Clear();
        foreach (var connection in _session.GetConnectionsForPage(_page.Id))
        {
            Connections.Add(new ConnectionViewItem
            {
                ConnectionId = connection.Id,
                FromDevicePinId = connection.FromDevicePinId,
                ToDevicePinId = connection.ToDevicePinId,
                WireNumber = connection.WireNumber,
            });
        }
        RedrawRequested?.Invoke();
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
        _session.PlacementsChanged -= OnSessionPlacementsChanged;
        _session.ConnectionsChanged -= OnSessionConnectionsChanged;
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
