using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ecad.App.Canvas;
using Ecad.App.Services;
using Ecad.App.Views;
using Ecad.Core.Models;
using Ecad.Core.ValueObjects;
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

    // Keyed by placement id, holding every dragged placement's position at drag-start — a
    // single-item drag is just the Count==1 case of the same mechanism a multi-select group drag
    // uses, so HandleMouseMove/HandleLeftButtonUp share one code path per branch instead of two.
    private readonly Dictionary<long, (double X, double Y)> _dragGroupOriginalPositions = new();

    private bool _isPanning;
    private (double X, double Y) _panStartScreen;
    private (double PanX, double PanY) _panStartOffset;

    private bool _isRubberBandSelecting;
    private bool _isRubberBandArmed;
    private (double X, double Y) _rubberBandStartScreen;
    private (double X, double Y) _rubberBandCurrentScreen;
    private const double RubberBandDragThreshold = 4; // screen pixels — matches SchematicPageView's palette drag-start threshold

    // Wire-drawing drag state (M7) — parallel to the placement-drag/pan state above, not replacing it.
    private long? _wireDrawFromDevicePinId;
    private WorldPoint _wireDrawCurrentWorld;

    // Dragging an EXISTING definition point — an independent entity now, so this just live-mutates its
    // own X/Y (same pattern as a placement drag) until drop, when the nearest wire under the cursor (if
    // any, within WireHitTolerance) becomes its new attachment (same wire = no change, different wire =
    // attach via MoveDefinitionPointCommand, no wire = detach).
    private long? _draggingDefinitionPointId;
    private (double X, double Y) _draggingDefinitionPointOriginalPosition;
    private long? _draggingDefinitionPointOriginalConnectionId;
    private const double DefinitionPointTickHitRadius = 8; // screen pixels — the tick's own rendered half-length is 5px

    // Drawing a brand-new cable line — mousedown-anywhere (no pin/wire gating, unlike wire-drawing)
    // starts it; mouseup finalizes the second endpoint and detects crossings.
    private WorldPoint? _drawingCableLineFromWorld;
    private WorldPoint _cableLineDrawCurrentWorld;

    // Dragging an EXISTING cable line — either the whole line (both endpoints translate together) or
    // just one endpoint (extends/shrinks it) — then re-detects crossings at the drop position (same
    // cable, new wires reached along the way pick up a fresh core).
    private long? _draggingCableLineId;
    private int _draggingCableLineEndpointIndex; // 0 = whole line (translate), 1 = P1, 2 = P2
    private (double X1, double Y1, double X2, double Y2) _draggingCableLineOriginalGeometry;
    private (double X, double Y) _cableLineDragStartWorld;

    private const int PaletteThumbnailSize = 48;
    private const double PinHitTolerance = 6; // world units — half a grid cell
    private const double WireHitTolerance = 4;
    private const double PinSnapRadius = 12; // world units — magnetic pull toward a nearby pin while placing/dragging

    public CanvasViewport Viewport { get; } = new();
    public ObservableCollection<PlacementViewItem> Placements { get; } = [];
    public ObservableCollection<ConnectionViewItem> Connections { get; } = [];
    public ObservableCollection<DefinitionPointViewItem> DefinitionPoints { get; } = [];
    public ObservableCollection<CableLineViewItem> CableLines { get; } = [];
    public IReadOnlyList<PaletteItem> PaletteItems { get; }
    public IReadOnlyList<double> GridSpacingOptions { get; } = [10, 20, 40, 80];

    /// <summary>Set by SchematicPageWindow's code-behind after construction, so dialogs (DeviceTagDialog) can center on it.</summary>
    public Window? OwnerWindow { get; set; }

    /// <summary>Mirrors Viewport.GridSpacing for two-way binding (CanvasViewport itself stays a plain
    /// POCO with no MVVM/WPF dependency, matching Ecad.Rendering's existing framework-agnostic design —
    /// see SchematicCanvasRenderer's own doc comment). Changing this only moves where grid dots are
    /// drawn and where SnapToGrid rounds a NEW/dragged position to; every existing Placement's X/Y is
    /// already stored in absolute world units, so nothing already placed moves when this changes.</summary>
    [ObservableProperty]
    private double _gridSpacing = 20.0;

    partial void OnGridSpacingChanged(double value)
    {
        Viewport.GridSpacing = value;
        RedrawRequested?.Invoke();
    }

    [ObservableProperty]
    private LoadedSymbol? _selectedPaletteSymbol;

    /// <summary>The "Place Definition Point" toolbar toggle — while true, the next left-click attempts
    /// to place/reposition a wire's definition point instead of selecting/dragging/placing a symbol,
    /// mirroring how SelectedPaletteSymbol takes over the next click while it's set. The two are
    /// mutually exclusive (see OnIsPlacingDefinitionPointChanged/SelectPaletteSymbol).</summary>
    [ObservableProperty]
    private bool _isPlacingDefinitionPoint;

    partial void OnIsPlacingDefinitionPointChanged(bool value)
    {
        if (value) { SelectedPaletteSymbol = null; IsDrawingCableLine = false; }
    }

    /// <summary>The "Draw Cable Line" toolbar toggle — while true, the next left-click-drag draws a
    /// cable definition line instead of selecting/dragging/placing anything else. Mutually exclusive
    /// with SelectedPaletteSymbol/IsPlacingDefinitionPoint, same pattern as those two already share.</summary>
    [ObservableProperty]
    private bool _isDrawingCableLine;

    partial void OnIsDrawingCableLineChanged(bool value)
    {
        if (value) { SelectedPaletteSymbol = null; IsPlacingDefinitionPoint = false; }
    }

    [ObservableProperty]
    private long? _selectedPlacementId;

    /// <summary>The rubber-band multi-select's members. Populated only when more than one placement
    /// is selected together — a single click-selection still goes through SelectedPlacementId alone,
    /// leaving this empty. Rotate (R) deliberately stays keyed off SelectedPlacementId only, so it's
    /// a no-op while a multi-selection is active.</summary>
    public HashSet<long> SelectedPlacementIds { get; } = [];

    /// <summary>Selected definition points, keyed by the point's own Id — an independent, symbol-like
    /// entity (see DefinitionPointViewItem), not a property of any connection. A wire/connection itself
    /// is never selectable (it has no independent identity to delete; the only way to remove one is to
    /// change the two symbols' geometry that produces it). Populated by a single click on a tick or by
    /// rubber-band (alongside SelectedPlacementIds, from the same drag).</summary>
    public HashSet<long> SelectedDefinitionPointIds { get; } = [];

    /// <summary>Selected cable lines, keyed by the line's own Id — same shape as SelectedDefinitionPointIds.
    /// Populated by a single click or right-click; rubber-band inclusion is not built for cable lines.</summary>
    public HashSet<long> SelectedCableLineIds { get; } = [];

    /// <summary>Selected cable line crossings, keyed by the crossing's own Id — same shape as
    /// SelectedDefinitionPointIds, including rubber-band inclusion. Independently selectable/rotatable/
    /// editable from the line itself, even though its position is resolved live rather than stored.</summary>
    public HashSet<long> SelectedCableLineCrossingIds { get; } = [];

    [ObservableProperty]
    private string _statusText = "Select a symbol from the palette to place it, click a pin to draw a wire, or click a placement to select it.";

    // Docked device-properties panel (M10 Phase 4) — replaces the modal DeviceTagDialog for the
    // rename-an-existing-placement path only (double-click). New-placement's tag prompt and
    // wire-rename both stay their existing modal dialogs, unaffected — see ADR-016/DECISIONS.md.
    private long _devicePanelPlacementId;
    private long _devicePanelDeviceId;
    private string? _devicePanelOriginalFunction;
    private string? _devicePanelOriginalLocation;
    private string _devicePanelOriginalDesignation = string.Empty;

    [ObservableProperty]
    private bool _isDevicePanelOpen;

    [ObservableProperty]
    private string _devicePanelFunction = string.Empty;

    [ObservableProperty]
    private string _devicePanelLocation = string.Empty;

    [ObservableProperty]
    private string _devicePanelDesignation = string.Empty;

    [ObservableProperty]
    private string _devicePanelPreviewTag = string.Empty;

    [ObservableProperty]
    private string _devicePanelValidationText = string.Empty;

    partial void OnDevicePanelFunctionChanged(string value) => UpdateDevicePanelPreview();
    partial void OnDevicePanelLocationChanged(string value) => UpdateDevicePanelPreview();
    partial void OnDevicePanelDesignationChanged(string value) => UpdateDevicePanelPreview();

    private void UpdateDevicePanelPreview()
    {
        var tag = new DeviceTag(NullIfBlank(DevicePanelFunction), NullIfBlank(DevicePanelLocation), NullIfBlank(DevicePanelDesignation) ?? string.Empty);
        DevicePanelPreviewTag = tag.ToString();
        DevicePanelValidationText = string.Empty;
    }

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
        _session.DefinitionPointsChanged += OnSessionDefinitionPointsChanged;
        _session.CableLinesChanged += OnSessionCableLinesChanged;
        AppSettingsStore.SettingsChanged += OnAppSettingsChanged;

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
            });
        }

        foreach (var point in _session.GetDefinitionPoints(_page.Id))
        {
            DefinitionPoints.Add(new DefinitionPointViewItem
            {
                Id = point.Id,
                X = point.X,
                Y = point.Y,
                WireNumber = point.WireNumber,
                Color = point.Color,
                CrossSectionMm2 = point.CrossSectionMm2,
                ConnectionId = point.ConnectionId,
                RotationDegrees = point.RotationDegrees,
            });
        }

        foreach (var line in _session.GetCableLines(_page.Id))
        {
            var cableLineItem = new CableLineViewItem
            {
                Id = line.Id,
                X1 = line.X1,
                Y1 = line.Y1,
                X2 = line.X2,
                Y2 = line.Y2,
                CableId = line.CableId,
                CableTag = _session.GetCable(line.CableId)?.Tag ?? string.Empty,
            };
            RefreshCableLineCrossings(cableLineItem);
            CableLines.Add(cableLineItem);
        }

        if (focusPlacementId is { } id && Placements.Any(p => p.PlacementId == id))
        {
            SelectedPlacementId = id;
            _pendingCenterPlacementId = id;
        }
    }

    [RelayCommand]
    private void SelectPaletteSymbol(LoadedSymbol symbol)
    {
        IsPlacingDefinitionPoint = false;
        IsDrawingCableLine = false;
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

    /// <summary>Places a symbol dragged-and-dropped from the palette at this screen position — the
    /// same placement logic (tag dialog, undo, auto-connect) the click-to-place flow uses, just
    /// triggered by a drop instead of a pre-selected palette symbol.</summary>
    public void PlaceSymbolAtScreenPosition(LoadedSymbol symbol, double screenX, double screenY) =>
        PlaceSymbolAt(symbol, screenX, screenY);

    public void HandleLeftButtonDown(double screenX, double screenY, bool ctrlPressed = false)
    {
        if (IsPlacingDefinitionPoint)
        {
            TryPlaceDefinitionPointAt(screenX, screenY);
            return;
        }

        if (IsDrawingCableLine)
        {
            var (startWorldX, startWorldY) = Viewport.ScreenToWorld(screenX, screenY);
            var (snappedStartX, snappedStartY) = Viewport.SnapToGrid(startWorldX, startWorldY);
            _drawingCableLineFromWorld = new WorldPoint(snappedStartX, snappedStartY);
            _cableLineDrawCurrentWorld = _drawingCableLineFromWorld.Value;
            StatusText = "Drag to draw the cable line.";
            RedrawRequested?.Invoke();
            return;
        }

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
            ClearSelection();
            StatusText = "Drag to another pin to draw a wire.";
            NotifySelectionCommandsChanged();
            RedrawRequested?.Invoke();
            return;
        }

        // An existing definition point's tick is a small, precise target — checked before the broader
        // placement/wire hit tests so it's reliably grabbable even where it happens to sit close to
        // either. Screen-space tolerance, since the tick's own rendered size is fixed in screen pixels
        // regardless of zoom.
        if (HitTestDefinitionPointTick(screenX, screenY) is { } tickId)
        {
            var tickItem = DefinitionPoints.First(p => p.Id == tickId);
            _draggingDefinitionPointId = tickId;
            _draggingDefinitionPointOriginalPosition = (tickItem.X, tickItem.Y);
            _draggingDefinitionPointOriginalConnectionId = tickItem.ConnectionId;
            ClearSelection();
            SelectedDefinitionPointIds.Add(tickId);
            NotifySelectionCommandsChanged();
            StatusText = "Drag to move the definition point.";
            RedrawRequested?.Invoke();
            return;
        }

        // A cable line crossing's tick — selectable (and rotatable/editable once selected) but not
        // draggable, since its position is resolved live from the line + wire geometry, not stored.
        if (HitTestCableLineCrossingTick(screenX, screenY) is { } crossingTickId)
        {
            ClearSelection();
            SelectedCableLineCrossingIds.Add(crossingTickId);
            NotifySelectionCommandsChanged();
            StatusText = "Selected cable core.";
            RedrawRequested?.Invoke();
            return;
        }

        // An existing cable line's endpoint is a small, precise target — checked before the broader
        // whole-line hit test so you can grab it to extend/shrink the line, same precedence idea as a
        // definition point's tick being checked before the wire it sits on.
        if (HitTestCableLineEndpointAt(screenX, screenY) is { } endpointHit)
        {
            var endpointCableLine = CableLines.First(c => c.Id == endpointHit.CableLineId);
            _draggingCableLineId = endpointHit.CableLineId;
            _draggingCableLineEndpointIndex = endpointHit.EndpointIndex;
            _draggingCableLineOriginalGeometry = (endpointCableLine.X1, endpointCableLine.Y1, endpointCableLine.X2, endpointCableLine.Y2);
            ClearSelection();
            SelectedCableLineIds.Add(endpointHit.CableLineId);
            NotifySelectionCommandsChanged();
            StatusText = "Drag to extend the cable line.";
            RedrawRequested?.Invoke();
            return;
        }

        // An existing cable line, same precise-target-first ordering as a definition point's tick.
        if (HitTestCableLineAt(worldPoint) is { } cableLineId)
        {
            var cableLineItem = CableLines.First(c => c.Id == cableLineId);
            _draggingCableLineId = cableLineId;
            _draggingCableLineEndpointIndex = 0;
            _draggingCableLineOriginalGeometry = (cableLineItem.X1, cableLineItem.Y1, cableLineItem.X2, cableLineItem.Y2);
            _cableLineDragStartWorld = (worldPoint.X, worldPoint.Y);
            ClearSelection();
            SelectedCableLineIds.Add(cableLineId);
            NotifySelectionCommandsChanged();
            StatusText = "Drag to move the cable line.";
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

            // Clicking a member of an already-active multi-selection starts a group drag that
            // preserves the whole set; clicking anything else (including a different placement
            // entirely) collapses back to an ordinary single selection.
            if (SelectedPlacementIds.Count > 1 && SelectedPlacementIds.Contains(placementId))
            {
                _dragPlacementId = placementId;
                _dragStartWorld = (worldPoint.X, worldPoint.Y);
                _dragGroupOriginalPositions.Clear();
                foreach (var id in SelectedPlacementIds)
                {
                    var groupItem = Placements.First(p => p.PlacementId == id);
                    _dragGroupOriginalPositions[id] = (groupItem.X, groupItem.Y);
                }
                StatusText = $"{SelectedPlacementIds.Count} placements selected.";
                RedrawRequested?.Invoke();
                return;
            }

            SelectedPlacementIds.Clear();
            SelectedPlacementId = placementId;
            SelectedDefinitionPointIds.Clear();
            _dragPlacementId = placementId;
            _dragStartWorld = (worldPoint.X, worldPoint.Y);
            _dragGroupOriginalPositions.Clear();
            _dragGroupOriginalPositions[placementId] = (item.X, item.Y);
            StatusText = $"Selected {item.DeviceTag}.";
            NotifySelectionCommandsChanged();
            RedrawRequested?.Invoke();
            return;
        }

        // A connection/wire has no independent identity to select or delete — it's a pure derived fact
        // of two symbols' geometry (same principle ADR-015 already established for the Grid Editor,
        // extended here to the canvas); only its definition point (handled above) is ever selectable.
        // A click that misses everything else just clears selection — but it might also be the start
        // of a drag, which turns into a rubber-band marquee once the mouse moves far enough
        // (HandleMouseMove). Arming here rather than starting the box immediately preserves "click a
        // definition point to select it" for a stationary click.
        ClearSelection();
        StatusText = "Nothing selected.";
        _isRubberBandArmed = true;
        _rubberBandStartScreen = (screenX, screenY);
        NotifySelectionCommandsChanged();
        RedrawRequested?.Invoke();
    }

    /// <summary>Right-click selects whatever's under the cursor (without starting a drag) so the
    /// context menu that follows acts on the right target — same hit-test order as a left click.
    /// Right-clicking a member of an already-active multi-selection keeps the whole selection intact
    /// (so Delete still applies to the group); right-clicking empty space leaves the current selection
    /// untouched, so a general Undo/Redo menu doesn't unexpectedly clear whatever was selected.</summary>
    public void HandleRightClick(double screenX, double screenY)
    {
        var placementHit = PlacementHitTester.HitTest(BuildHitTestList(), Viewport, screenX, screenY);
        if (placementHit is { } placementId)
        {
            if (!(SelectedPlacementIds.Count > 1 && SelectedPlacementIds.Contains(placementId)))
            {
                SelectedPlacementIds.Clear();
                SelectedPlacementId = placementId;
                SelectedDefinitionPointIds.Clear();
            }
            NotifySelectionCommandsChanged();
            RedrawRequested?.Invoke();
            return;
        }

        if (HitTestDefinitionPointTick(screenX, screenY) is { } tickId)
        {
            ClearSelection();
            SelectedDefinitionPointIds.Add(tickId);
            NotifySelectionCommandsChanged();
            RedrawRequested?.Invoke();
            return;
        }

        if (HitTestCableLineCrossingTick(screenX, screenY) is { } crossingTickId)
        {
            ClearSelection();
            SelectedCableLineCrossingIds.Add(crossingTickId);
            NotifySelectionCommandsChanged();
            RedrawRequested?.Invoke();
            return;
        }

        var (rightClickWorldX, rightClickWorldY) = Viewport.ScreenToWorld(screenX, screenY);
        if (HitTestCableLineAt(new WorldPoint(rightClickWorldX, rightClickWorldY)) is { } cableLineId)
        {
            ClearSelection();
            SelectedCableLineIds.Add(cableLineId);
            NotifySelectionCommandsChanged();
            RedrawRequested?.Invoke();
        }
    }

    private void ClearSelection()
    {
        SelectedPlacementIds.Clear();
        SelectedPlacementId = null;
        SelectedDefinitionPointIds.Clear();
        SelectedCableLineIds.Clear();
        SelectedCableLineCrossingIds.Clear();
    }

    private void NotifySelectionCommandsChanged()
    {
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        RotateSelectionCommand.NotifyCanExecuteChanged();
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

    /// <summary>Populates and opens the docked device panel for a placement's tag — re-populating
    /// while already open (double-clicking a different placement) just overwrites the previous
    /// contents, no explicit close-first step needed.</summary>
    private void OpenDevicePanel(long placementId, long deviceId, string? function, string? location, string designation)
    {
        _devicePanelPlacementId = placementId;
        _devicePanelDeviceId = deviceId;
        _devicePanelOriginalFunction = function;
        _devicePanelOriginalLocation = location;
        _devicePanelOriginalDesignation = designation;

        DevicePanelFunction = function ?? string.Empty;
        DevicePanelLocation = location ?? string.Empty;
        DevicePanelDesignation = designation;
        IsDevicePanelOpen = true;
    }

    [RelayCommand]
    private void ApplyDeviceEdit()
    {
        var designation = NullIfBlank(DevicePanelDesignation);
        if (designation is null)
        {
            DevicePanelValidationText = "Designation is required.";
            return;
        }

        var function = NullIfBlank(DevicePanelFunction);
        var location = NullIfBlank(DevicePanelLocation);

        if (function == _devicePanelOriginalFunction && location == _devicePanelOriginalLocation && designation == _devicePanelOriginalDesignation)
        {
            IsDevicePanelOpen = false;
            return;
        }

        if (!IsTagAvailable(function, location, designation, _devicePanelDeviceId))
        {
            DevicePanelValidationText = $"Tag '{new DeviceTag(function, location, designation)}' is already used in this project.";
            return;
        }

        _undoRedo.Execute(new RenameTagCommand(_session, this, _devicePanelPlacementId, _devicePanelDeviceId,
            _devicePanelOriginalFunction, _devicePanelOriginalLocation, _devicePanelOriginalDesignation, function, location, designation));
        NotifyUndoRedoChanged();
        StatusText = $"Renamed to {designation}.";
        IsDevicePanelOpen = false;
        RedrawRequested?.Invoke();
    }

    [RelayCommand]
    private void CloseDevicePanel() => IsDevicePanelOpen = false;

    private static string? NullIfBlank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Double-click on a placement renames its device tag; double-click on an existing
    /// definition point's tick opens it for editing/removal. Placing a brand-new one is exclusively
    /// the "Place Definition Point" toolbar tool's job (TryPlaceDefinitionPointAt) — double-clicking a
    /// bare wire (or empty space) is a no-op. Editing the symbol's geometry itself is the separate
    /// symbol editor (deferred to Phase 2, out of scope here).</summary>
    public void HandleDoubleClick(double screenX, double screenY)
    {
        var placementHit = PlacementHitTester.HitTest(BuildHitTestList(), Viewport, screenX, screenY);
        if (placementHit is { } placementId)
        {
            var item = Placements.First(p => p.PlacementId == placementId);
            OpenDevicePanel(placementId, item.DeviceId, item.Function, item.Location, item.DeviceTag);
            return;
        }

        if (HitTestDefinitionPointTick(screenX, screenY) is { } definitionPointId)
        {
            OpenExistingDefinitionPointDialog(definitionPointId);
            return;
        }

        if (HitTestCableLineCrossingTick(screenX, screenY) is { } crossingId)
        {
            OpenCableCoreDialog(crossingId);
            return;
        }

        var (worldX, worldY) = Viewport.ScreenToWorld(screenX, screenY);
        if (HitTestCableLineAt(new WorldPoint(worldX, worldY)) is { } cableLineId)
            OpenExistingCableLineDialog(cableLineId);
    }

    /// <summary>Opens CableCoreDialog for an existing cable line crossing — pre-filled with its
    /// current core number/color/cross-section, editing the underlying CableCore directly (mirrored
    /// onto the crossed Connection if still live).</summary>
    private void OpenCableCoreDialog(long crossingId)
    {
        var (line, crossing) = FindCableLineCrossing(crossingId);

        var dialog = new CableCoreDialog(crossing.CoreNumber, crossing.Color, crossing.CrossSectionMm2,
            coreNumber => _session.IsCableCoreNumberAvailable(line.CableId, coreNumber, crossing.CableCoreId))
        {
            Owner = OwnerWindow,
        };
        if (dialog.ShowDialog() != true) return;

        _undoRedo.Execute(new SetCableLineCrossingCoreCommand(_session, this, crossingId,
            crossing.CoreNumber, crossing.Color, crossing.CrossSectionMm2,
            dialog.CoreNumber, dialog.Color, dialog.CrossSectionMm2));
        NotifyUndoRedoChanged();
        StatusText = "Cable core updated.";
        RedrawRequested?.Invoke();
    }

    /// <summary>Converts a "Place Definition Point"-mode click into a brand-new, independent definition
    /// point at that exact spot — snapped onto the nearest wire within tolerance if one's close by
    /// (WireHitTester.HitTestWireForDefinitionPoint), attaching to it; otherwise dropped at the raw
    /// click position with no attachment at all, since a definition point is a free-standing,
    /// symbol-like entity that doesn't require a wire to exist.</summary>
    private void TryPlaceDefinitionPointAt(double screenX, double screenY)
    {
        IsPlacingDefinitionPoint = false;

        var (worldX, worldY) = Viewport.ScreenToWorld(screenX, screenY);
        var clickPoint = new WorldPoint(worldX, worldY);
        var pinPositions = BuildPinPositions();
        var pinPositionsById = pinPositions.ToDictionary(p => p.DevicePinId, p => p.Position);
        var wires = BuildWireHitTestList(pinPositionsById);
        var hit = WireHitTester.HitTestWireForDefinitionPoint(clickPoint, wires, WireHitTolerance);

        if (hit is { } result)
        {
            var wire = wires.First(w => w.ConnectionId == result.ConnectionId);
            var snapped = RouteMath.PointAtT(wire.Route, result.PositionT);
            OpenNewDefinitionPointDialog(snapped.X, snapped.Y, result.ConnectionId);
        }
        else
        {
            var (snappedX, snappedY) = Viewport.SnapToGrid(clickPoint.X, clickPoint.Y);
            OpenNewDefinitionPointDialog(snappedX, snappedY, null);
        }
    }

    /// <summary>Finds an already-placed definition point's tick within a small screen-space radius of
    /// the click — the start of a drag-to-move gesture (HandleLeftButtonUp finishes it) or the target
    /// of a double-click-to-edit. Deliberately screen-space, not world-space: the tick's rendered size
    /// never changes with zoom, so neither should the radius used to grab it. Trivial now that a
    /// definition point carries its own absolute X/Y — no pin/route lookup needed at all.</summary>
    private long? HitTestDefinitionPointTick(double screenX, double screenY)
    {
        foreach (var point in DefinitionPoints)
        {
            var (tickScreenX, tickScreenY) = Viewport.WorldToScreen(point.X, point.Y);
            var dx = tickScreenX - screenX;
            var dy = tickScreenY - screenY;
            if (Math.Sqrt(dx * dx + dy * dy) <= DefinitionPointTickHitRadius) return point.Id;
        }
        return null;
    }

    /// <summary>Opens DefinitionPointDialog for a brand-new definition point at the given world
    /// position — pre-filled with a fresh suggested wire number, Remove hidden. Applies the result as
    /// one PlaceDefinitionPointCommand; cancelling leaves nothing behind.</summary>
    private void OpenNewDefinitionPointDialog(double x, double y, long? connectionId)
    {
        var dialog = new DefinitionPointDialog(_session.SuggestNextWireNumber(), null, null, isExisting: false,
            wireNumber => _session.IsWireNumberAvailable(wireNumber, excludingDefinitionPointId: null))
        {
            Owner = OwnerWindow,
        };
        if (dialog.ShowDialog() != true)
        {
            StatusText = "Definition point placement cancelled.";
            return;
        }

        _undoRedo.Execute(new PlaceDefinitionPointCommand(_session, this, _page.Id, x, y, dialog.WireNumber, dialog.Color, dialog.CrossSectionMm2, connectionId));
        NotifyUndoRedoChanged();
        StatusText = "Definition point placed.";
        RedrawRequested?.Invoke();
    }

    /// <summary>Opens DefinitionPointDialog for an existing definition point — pre-filled with its
    /// current data, Remove shown. Applies an edit as one SetDefinitionPointDataCommand, or a removal
    /// as one DeleteDefinitionPointCommand.</summary>
    private void OpenExistingDefinitionPointDialog(long definitionPointId)
    {
        var item = DefinitionPoints.First(p => p.Id == definitionPointId);

        var dialog = new DefinitionPointDialog(item.WireNumber, item.Color, item.CrossSectionMm2, isExisting: true,
            wireNumber => _session.IsWireNumberAvailable(wireNumber, excludingDefinitionPointId: definitionPointId))
        {
            Owner = OwnerWindow,
        };
        if (dialog.ShowDialog() != true) return;

        if (dialog.Removed)
        {
            _undoRedo.Execute(new DeleteDefinitionPointCommand(_session, this, _page.Id, item));
            NotifyUndoRedoChanged();
            SelectedDefinitionPointIds.Remove(definitionPointId);
            NotifySelectionCommandsChanged();
            StatusText = "Definition point removed.";
            RedrawRequested?.Invoke();
            return;
        }

        _undoRedo.Execute(new SetDefinitionPointDataCommand(_session, this, definitionPointId,
            item.WireNumber, item.Color, item.CrossSectionMm2, dialog.WireNumber, dialog.Color, dialog.CrossSectionMm2));
        NotifyUndoRedoChanged();
        StatusText = "Definition point updated.";
        RedrawRequested?.Invoke();
    }

    /// <summary>Middle-click drag pans the canvas — right-click is a context menu (see
    /// HandleRightClick) and left-click-drag on empty space is the rubber-band multi-select below, so
    /// panning needed a third button of its own.</summary>
    public void HandlePanStart(double screenX, double screenY)
    {
        _isPanning = true;
        _panStartScreen = (screenX, screenY);
        _panStartOffset = (Viewport.PanX, Viewport.PanY);
    }

    public void HandlePanEnd() => _isPanning = false;

    /// <summary>Resets any in-progress pan/rubber-band/placement-drag/wire-draw state without
    /// committing anything — called if mouse capture is lost mid-drag (e.g. focus stolen by another
    /// window), so the interaction can't get stuck waiting for a MouseUp that will never arrive. A
    /// placement drag reverts to its pre-drag position, same as a normal drag that ends up back where
    /// it started (HandleLeftButtonUp's own no-op case).</summary>
    public void CancelActiveDrag()
    {
        _isPanning = false;
        _isRubberBandArmed = false;
        _isRubberBandSelecting = false;
        _wireDrawFromDevicePinId = null;

        if (_draggingDefinitionPointId is { } draggingId)
        {
            var draggedItem = DefinitionPoints.FirstOrDefault(p => p.Id == draggingId);
            if (draggedItem is not null)
            {
                draggedItem.X = _draggingDefinitionPointOriginalPosition.X;
                draggedItem.Y = _draggingDefinitionPointOriginalPosition.Y;
            }
            _draggingDefinitionPointId = null;
        }

        _drawingCableLineFromWorld = null;
        IsDrawingCableLine = false;

        if (_draggingCableLineId is { } draggingCableLineId)
        {
            var draggedCableLine = CableLines.FirstOrDefault(c => c.Id == draggingCableLineId);
            if (draggedCableLine is not null)
            {
                draggedCableLine.X1 = _draggingCableLineOriginalGeometry.X1;
                draggedCableLine.Y1 = _draggingCableLineOriginalGeometry.Y1;
                draggedCableLine.X2 = _draggingCableLineOriginalGeometry.X2;
                draggedCableLine.Y2 = _draggingCableLineOriginalGeometry.Y2;
            }
            _draggingCableLineId = null;
        }

        if (_dragPlacementId is not null)
        {
            foreach (var (id, original) in _dragGroupOriginalPositions)
            {
                var item = Placements.FirstOrDefault(p => p.PlacementId == id);
                if (item is not null) { item.X = original.X; item.Y = original.Y; }
            }
            _dragPlacementId = null;
            _dragGroupOriginalPositions.Clear();
        }

        RedrawRequested?.Invoke();
    }

    /// <summary>Finalizes a left-click-drag rubber-band selection into an actual multi-select — called
    /// once HandleMouseMove has confirmed the drag crossed RubberBandDragThreshold (see
    /// HandleLeftButtonDown/HandleMouseMove/HandleLeftButtonUp's _isRubberBandArmed/_isRubberBandSelecting
    /// handshake).</summary>
    private void FinishRubberBandSelection()
    {
        var (worldX1, worldY1) = Viewport.ScreenToWorld(_rubberBandStartScreen.X, _rubberBandStartScreen.Y);
        var (worldX2, worldY2) = Viewport.ScreenToWorld(_rubberBandCurrentScreen.X, _rubberBandCurrentScreen.Y);

        // AutoCAD-style direction rule: dragging left-to-right is a "window" select (a placement must
        // be fully enclosed); dragging right-to-left is a "crossing" select (touching the rectangle at
        // all is enough) — lets a right-to-left drag grab a placement that only partially fits on
        // screen without needing to enclose it completely. Definition points are single points, not
        // boxes, so that distinction doesn't apply to them — "inside the rectangle" is unambiguous.
        var draggedLeftToRight = _rubberBandCurrentScreen.X >= _rubberBandStartScreen.X;
        var placementHits = PlacementHitTester.HitTestRect(BuildHitTestList(), worldX1, worldY1, worldX2, worldY2, requireFullContainment: draggedLeftToRight);
        var minX = Math.Min(worldX1, worldX2);
        var minY = Math.Min(worldY1, worldY2);
        var maxX = Math.Max(worldX1, worldX2);
        var maxY = Math.Max(worldY1, worldY2);
        var definitionPointHits = HitTestDefinitionPointsInRect(minX, minY, maxX, maxY);
        var cableLineCrossingHits = HitTestCableLineCrossingsInRect(minX, minY, maxX, maxY);

        ClearSelection();
        foreach (var id in placementHits) SelectedPlacementIds.Add(id);
        foreach (var id in definitionPointHits) SelectedDefinitionPointIds.Add(id);
        foreach (var id in cableLineCrossingHits) SelectedCableLineCrossingIds.Add(id);
        if (placementHits.Count == 1 && definitionPointHits.Count == 0 && cableLineCrossingHits.Count == 0) SelectedPlacementId = placementHits[0];

        StatusText = placementHits.Count + definitionPointHits.Count + cableLineCrossingHits.Count == 0
            ? "Nothing selected."
            : BuildRubberBandSelectionSummary(placementHits.Count, definitionPointHits.Count, cableLineCrossingHits.Count);
        NotifySelectionCommandsChanged();
        RedrawRequested?.Invoke();
    }

    private static string BuildRubberBandSelectionSummary(int placementCount, int definitionPointCount, int cableLineCrossingCount)
    {
        var parts = new List<string>();
        if (placementCount > 0) parts.Add($"{placementCount} placement{(placementCount == 1 ? "" : "s")}");
        if (definitionPointCount > 0) parts.Add($"{definitionPointCount} definition point{(definitionPointCount == 1 ? "" : "s")}");
        if (cableLineCrossingCount > 0) parts.Add($"{cableLineCrossingCount} cable core{(cableLineCrossingCount == 1 ? "" : "s")}");
        return string.Join(", ", parts) + " selected.";
    }

    /// <summary>Every definition point whose own position falls inside the given world-space rectangle
    /// — the rubber-band's definition-point query, parallel to PlacementHitTester.HitTestRect but
    /// simpler since a definition point is a single point, not a box (no window-vs-crossing distinction
    /// needed), and trivial now that it carries its own absolute X/Y directly.</summary>
    private List<long> HitTestDefinitionPointsInRect(double minX, double minY, double maxX, double maxY)
    {
        var hits = new List<long>();
        foreach (var point in DefinitionPoints)
        {
            if (point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY)
                hits.Add(point.Id);
        }
        return hits;
    }

    /// <summary>Finalizes a definition-point drag: whichever wire ends up nearest the drop point (if
    /// any, within WireHitTolerance) becomes its new attachment — snapped onto that wire's route.
    /// Dropping back where it started (same position, same attachment) is a no-op. Dropping on a
    /// different wire attaches it there via MoveDefinitionPointCommand, but only if that wire doesn't
    /// already have a different definition point attached — silently overwriting another wire's
    /// number/data would be a surprising way to lose it. Dropping off every wire just detaches it,
    /// leaving it exactly where it was left — it never disappears, only a placement's own dragged-off
    /// position mattered before; a definition point surviving detached is the entire point of it being
    /// an independent entity now (see DECISIONS.md).</summary>
    private void FinishDefinitionPointDrag(long definitionPointId)
    {
        var (fromX, fromY) = _draggingDefinitionPointOriginalPosition;
        var fromConnectionId = _draggingDefinitionPointOriginalConnectionId;
        _draggingDefinitionPointId = null;

        var item = DefinitionPoints.First(p => p.Id == definitionPointId);
        var dropWorld = new WorldPoint(item.X, item.Y);

        var pinPositions = BuildPinPositions();
        var pinPositionsById = pinPositions.ToDictionary(p => p.DevicePinId, p => p.Position);
        var wires = BuildWireHitTestList(pinPositionsById);
        var hit = WireHitTester.HitTestWireForDefinitionPoint(dropWorld, wires, WireHitTolerance);

        double toX;
        double toY;
        long? toConnectionId;
        if (hit is { } result)
        {
            if (result.ConnectionId != fromConnectionId
                && DefinitionPoints.Any(p => p.Id != definitionPointId && p.ConnectionId == result.ConnectionId))
            {
                item.X = fromX;
                item.Y = fromY;
                StatusText = "That wire already has a definition point.";
                RedrawRequested?.Invoke();
                return;
            }

            var wire = wires.First(w => w.ConnectionId == result.ConnectionId);
            var snapped = RouteMath.PointAtT(wire.Route, result.PositionT);
            toX = snapped.X;
            toY = snapped.Y;
            toConnectionId = result.ConnectionId;
        }
        else
        {
            toX = dropWorld.X;
            toY = dropWorld.Y;
            toConnectionId = null;
        }

        if (Math.Abs(toX - fromX) < 0.001 && Math.Abs(toY - fromY) < 0.001 && toConnectionId == fromConnectionId)
        {
            item.X = fromX;
            item.Y = fromY;
            RedrawRequested?.Invoke();
            return;
        }

        // Put it back to the drag-start position so MoveDefinitionPointCommand.Do() is the single
        // place that advances it, same "command is the sole advancer" rule MoveCommand's own path uses.
        item.X = fromX;
        item.Y = fromY;
        _undoRedo.Execute(new MoveDefinitionPointCommand(_session, this, definitionPointId, fromX, fromY, fromConnectionId, toX, toY, toConnectionId));
        NotifyUndoRedoChanged();
        StatusText = toConnectionId is not null ? "Definition point attached to wire." : "Definition point moved.";
        RedrawRequested?.Invoke();
    }

    /// <summary>Point-to-segment hit-test against every currently-loaded cable line — world-space
    /// tolerance (unlike a definition point's fixed-screen-pixel tick), since a cable line is a real
    /// world-space geometric object, same convention WireHitTester's own tolerance already uses.</summary>
    private long? HitTestCableLineAt(WorldPoint point)
    {
        var lines = CableLines.Select(c => new HitTestCableLine(c.Id, new WorldPoint(c.X1, c.Y1), new WorldPoint(c.X2, c.Y2))).ToList();
        return CableLineHitTester.HitTest(point, lines, WireHitTolerance);
    }

    /// <summary>Finds an already-drawn cable line's endpoint within a small screen-space radius of the
    /// click — the start of a drag-to-extend/shrink gesture. Screen-space, same reasoning as
    /// DefinitionPointTickHitRadius: the grabbable target should stay the same visual size regardless
    /// of zoom.</summary>
    private (long CableLineId, int EndpointIndex)? HitTestCableLineEndpointAt(double screenX, double screenY)
    {
        foreach (var line in CableLines)
        {
            var (p1ScreenX, p1ScreenY) = Viewport.WorldToScreen(line.X1, line.Y1);
            if (ScreenDistance(p1ScreenX, p1ScreenY, screenX, screenY) <= DefinitionPointTickHitRadius) return (line.Id, 1);

            var (p2ScreenX, p2ScreenY) = Viewport.WorldToScreen(line.X2, line.Y2);
            if (ScreenDistance(p2ScreenX, p2ScreenY, screenX, screenY) <= DefinitionPointTickHitRadius) return (line.Id, 2);
        }
        return null;
    }

    private static double ScreenDistance(double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Every Connection currently crossed by the given straight segment — the one shared
    /// crossing-detection call used when drawing, dragging, or undo-recreating a cable line.</summary>
    internal IReadOnlyList<long> DetectCableLineCrossings(double x1, double y1, double x2, double y2)
    {
        var pinPositions = BuildPinPositions();
        var pinPositionsById = pinPositions.ToDictionary(p => p.DevicePinId, p => p.Position);
        var lineStart = new WorldPoint(x1, y1);
        var lineEnd = new WorldPoint(x2, y2);
        var hits = new List<long>();

        foreach (var connection in Connections)
        {
            if (!pinPositionsById.TryGetValue(connection.FromDevicePinId, out var from)) continue;
            if (!pinPositionsById.TryGetValue(connection.ToDevicePinId, out var to)) continue;
            var route = OrthogonalRouter.Route(from, to);
            if (SegmentIntersection.IntersectRoute(lineStart, lineEnd, route) is not null)
                hits.Add(connection.ConnectionId);
        }

        return hits;
    }

    /// <summary>Finalizes a cable line drag: moves both endpoints together, then re-detects crossings at
    /// both the drag-start and drop positions so MoveCableLineCommand can undo/redo correctly. Dropping
    /// back exactly where it started is a no-op.</summary>
    private void FinishCableLineDrag(long cableLineId)
    {
        _draggingCableLineId = null;

        var item = CableLines.First(c => c.Id == cableLineId);
        var (fromX1, fromY1, fromX2, fromY2) = _draggingCableLineOriginalGeometry;
        var toX1 = item.X1;
        var toY1 = item.Y1;
        var toX2 = item.X2;
        var toY2 = item.Y2;

        if (Math.Abs(toX1 - fromX1) < 0.001 && Math.Abs(toY1 - fromY1) < 0.001 &&
            Math.Abs(toX2 - fromX2) < 0.001 && Math.Abs(toY2 - fromY2) < 0.001)
        {
            RedrawRequested?.Invoke();
            return;
        }

        // Put it back to the drag-start geometry so MoveCableLineCommand.Do() is the single place that
        // advances it, same "command is the sole advancer" rule MoveCommand's own path uses.
        item.X1 = fromX1;
        item.Y1 = fromY1;
        item.X2 = fromX2;
        item.Y2 = fromY2;

        var fromCrossedConnectionIds = DetectCableLineCrossings(fromX1, fromY1, fromX2, fromY2);
        var toCrossedConnectionIds = DetectCableLineCrossings(toX1, toY1, toX2, toY2);

        _undoRedo.Execute(new MoveCableLineCommand(_session, this, cableLineId,
            fromX1, fromY1, fromX2, fromY2, fromCrossedConnectionIds,
            toX1, toY1, toX2, toY2, toCrossedConnectionIds,
            item.CableTag));
        NotifyUndoRedoChanged();
        StatusText = "Cable line moved.";
        RedrawRequested?.Invoke();
    }

    /// <summary>Opens CableLineDialog for an existing cable line — pre-filled with its current Cable
    /// Tag, Remove shown. Changing the tag re-homes every live crossing to the found-or-created new
    /// cable (ReassignCableLineCommand); Remove deletes the line and clears every crossing's mirror.</summary>
    private void OpenExistingCableLineDialog(long cableLineId)
    {
        var item = CableLines.First(c => c.Id == cableLineId);
        var dialog = new CableLineDialog(item.CableTag, isExisting: true) { Owner = OwnerWindow };
        if (dialog.ShowDialog() != true) return;

        if (dialog.Removed)
        {
            _undoRedo.Execute(new DeleteCableLineCommand(_session, this, _page.Id, item));
            NotifyUndoRedoChanged();
            SelectedCableLineIds.Remove(cableLineId);
            NotifySelectionCommandsChanged();
            StatusText = "Cable line removed.";
            RedrawRequested?.Invoke();
            return;
        }

        if (string.Equals(dialog.CableTag, item.CableTag, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "Cable line unchanged.";
            return;
        }

        var crossedConnectionIds = DetectCableLineCrossings(item.X1, item.Y1, item.X2, item.Y2);
        _undoRedo.Execute(new ReassignCableLineCommand(_session, this, cableLineId, item.CableTag, dialog.CableTag, crossedConnectionIds));
        NotifyUndoRedoChanged();
        StatusText = "Cable line re-homed.";
        RedrawRequested?.Invoke();
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

        if (_draggingDefinitionPointId is { } draggingId)
        {
            var (worldX, worldY) = Viewport.ScreenToWorld(screenX, screenY);
            var (snappedX, snappedY) = Viewport.SnapToGrid(worldX, worldY);
            var draggingItem = DefinitionPoints.First(p => p.Id == draggingId);
            draggingItem.X = snappedX;
            draggingItem.Y = snappedY;
            RedrawRequested?.Invoke();
            return;
        }

        if (_drawingCableLineFromWorld is not null)
        {
            var (worldX, worldY) = Viewport.ScreenToWorld(screenX, screenY);
            var (snappedX, snappedY) = Viewport.SnapToGrid(worldX, worldY);
            _cableLineDrawCurrentWorld = new WorldPoint(snappedX, snappedY);
            RedrawRequested?.Invoke();
            return;
        }

        if (_draggingCableLineId is { } draggingCableLineId)
        {
            var (worldX, worldY) = Viewport.ScreenToWorld(screenX, screenY);
            var (snappedX, snappedY) = Viewport.SnapToGrid(worldX, worldY);
            var draggingCableLine = CableLines.First(c => c.Id == draggingCableLineId);

            if (_draggingCableLineEndpointIndex == 1)
            {
                draggingCableLine.X1 = snappedX;
                draggingCableLine.Y1 = snappedY;
            }
            else if (_draggingCableLineEndpointIndex == 2)
            {
                draggingCableLine.X2 = snappedX;
                draggingCableLine.Y2 = snappedY;
            }
            else
            {
                // Same "snap the anchor's candidate position, then derive the delta from that" trick
                // the placement group-drag uses — snapping the raw cursor delta on its own (as this
                // used to) doesn't guarantee the RESULT lands on the grid, since the cursor's own
                // start position was never itself grid-aligned; only the applied delta matters.
                var (snappedAnchorX, snappedAnchorY) = Viewport.SnapToGrid(
                    _draggingCableLineOriginalGeometry.X1 + (worldX - _cableLineDragStartWorld.X),
                    _draggingCableLineOriginalGeometry.Y1 + (worldY - _cableLineDragStartWorld.Y));
                var deltaX = snappedAnchorX - _draggingCableLineOriginalGeometry.X1;
                var deltaY = snappedAnchorY - _draggingCableLineOriginalGeometry.Y1;
                draggingCableLine.X1 = _draggingCableLineOriginalGeometry.X1 + deltaX;
                draggingCableLine.Y1 = _draggingCableLineOriginalGeometry.Y1 + deltaY;
                draggingCableLine.X2 = _draggingCableLineOriginalGeometry.X2 + deltaX;
                draggingCableLine.Y2 = _draggingCableLineOriginalGeometry.Y2 + deltaY;
            }
            RedrawRequested?.Invoke();
            return;
        }

        if (_isRubberBandSelecting)
        {
            _rubberBandCurrentScreen = (screenX, screenY);
            RedrawRequested?.Invoke();
            return;
        }

        // Armed (left button went down on empty space) but not yet dragging far enough to count as a
        // marquee — a plain click's tiny/zero movement never crosses this, leaving the click-time
        // wire-selection (or deselect) from HandleLeftButtonDown as the final outcome.
        if (_isRubberBandArmed)
        {
            var armedDeltaX = screenX - _rubberBandStartScreen.X;
            var armedDeltaY = screenY - _rubberBandStartScreen.Y;
            if (Math.Sqrt(armedDeltaX * armedDeltaX + armedDeltaY * armedDeltaY) >= RubberBandDragThreshold)
            {
                _isRubberBandSelecting = true;
                _rubberBandCurrentScreen = (screenX, screenY);
                RedrawRequested?.Invoke();
            }
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

        if (_dragPlacementId is not { } placementId) return;
        var (dragWorldX, dragWorldY) = Viewport.ScreenToWorld(screenX, screenY);

        // A group drag (Count>1) snaps the shared delta as a whole, so every member keeps its exact
        // relative offset — snapping each member independently (as the single-item branch below
        // does, for its magnet-pin pull) would let members drift apart from each other.
        if (_dragGroupOriginalPositions.Count > 1)
        {
            var anchorOriginal = _dragGroupOriginalPositions[placementId];
            var (snappedAnchorX, snappedAnchorY) = Viewport.SnapToGrid(
                anchorOriginal.X + (dragWorldX - _dragStartWorld.X),
                anchorOriginal.Y + (dragWorldY - _dragStartWorld.Y));
            var deltaX = snappedAnchorX - anchorOriginal.X;
            var deltaY = snappedAnchorY - anchorOriginal.Y;

            foreach (var (id, original) in _dragGroupOriginalPositions)
            {
                var groupItem = Placements.First(p => p.PlacementId == id);
                groupItem.X = original.X + deltaX;
                groupItem.Y = original.Y + deltaY;
            }
            RedrawRequested?.Invoke();
            return;
        }

        var dragOriginal = _dragGroupOriginalPositions[placementId];
        var (itemSnappedX, itemSnappedY) = Viewport.SnapToGrid(
            dragOriginal.X + (dragWorldX - _dragStartWorld.X),
            dragOriginal.Y + (dragWorldY - _dragStartWorld.Y));

        var item = Placements.First(p => p.PlacementId == placementId);
        var draggedPinIds = item.Pins.Select(p => p.DevicePinId).ToHashSet();
        var otherPins = BuildPinPositions().Where(p => !draggedPinIds.Contains(p.DevicePinId)).ToList();
        var symbol = _symbolsByName[item.SymbolName];
        var localPins = item.Pins
            .Select(pin => symbol.Definition.ConnectionPoints.FirstOrDefault(cp => cp.Pin == pin.Name))
            .Where(cp => cp is not null)
            .Select(cp => (cp!.X, cp.Y, cp.Direction));
        (itemSnappedX, itemSnappedY) = ApplyPinMagnetSnap(localPins, item.RotationDegrees, item.Mirrored, itemSnappedX, itemSnappedY, otherPins);

        item.X = itemSnappedX;
        item.Y = itemSnappedY;
        RedrawRequested?.Invoke();
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

        if (_draggingDefinitionPointId is { } draggingDefinitionPointId)
        {
            FinishDefinitionPointDrag(draggingDefinitionPointId);
            return;
        }

        if (_drawingCableLineFromWorld is { } startPoint)
        {
            _drawingCableLineFromWorld = null;
            IsDrawingCableLine = false;
            var endPoint = _cableLineDrawCurrentWorld;

            var crossedConnectionIds = DetectCableLineCrossings(startPoint.X, startPoint.Y, endPoint.X, endPoint.Y);
            if (crossedConnectionIds.Count == 0)
            {
                StatusText = "No wires crossed — cable line cancelled.";
                RedrawRequested?.Invoke();
                return;
            }

            var dialog = new CableLineDialog(_session.SuggestNextCableTag(), isExisting: false) { Owner = OwnerWindow };
            if (dialog.ShowDialog() != true)
            {
                StatusText = "Cable line cancelled.";
                RedrawRequested?.Invoke();
                return;
            }

            _undoRedo.Execute(new DrawCableLineCommand(_session, this, _page.Id, startPoint.X, startPoint.Y, endPoint.X, endPoint.Y, dialog.CableTag, crossedConnectionIds));
            NotifyUndoRedoChanged();
            StatusText = "Cable line drawn.";
            RedrawRequested?.Invoke();
            return;
        }

        if (_draggingCableLineId is { } draggingCableLineId)
        {
            FinishCableLineDrag(draggingCableLineId);
            return;
        }

        if (_isRubberBandArmed)
        {
            _isRubberBandArmed = false;
            if (_isRubberBandSelecting)
            {
                _isRubberBandSelecting = false;
                FinishRubberBandSelection();
            }
            return;
        }

        if (_dragPlacementId is not { } placementId) return;
        _dragPlacementId = null;

        if (_dragGroupOriginalPositions.Count > 1)
        {
            var movedIds = new List<long>();
            var commands = new List<IUndoableCommand>();
            foreach (var (id, original) in _dragGroupOriginalPositions)
            {
                var groupItem = Placements.First(p => p.PlacementId == id);
                var groupFinalX = groupItem.X;
                var groupFinalY = groupItem.Y;
                if (groupFinalX == original.X && groupFinalY == original.Y) continue;

                // Same "put it back so the command is the single place that advances it" rule as
                // the single-item path below, applied per group member.
                groupItem.X = original.X;
                groupItem.Y = original.Y;
                commands.Add(new MoveCommand(_session, this, id, original.X, original.Y, groupFinalX, groupFinalY));
                movedIds.Add(id);
            }

            if (commands.Count > 0)
            {
                _undoRedo.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand(commands));
                foreach (var id in movedIds) RunAutoConnect(id);
                NotifyUndoRedoChanged();
            }
            _dragGroupOriginalPositions.Clear();
            RedrawRequested?.Invoke();
            return;
        }

        var item = Placements.First(p => p.PlacementId == placementId);
        var finalX = item.X;
        var finalY = item.Y;
        var dragOriginal = _dragGroupOriginalPositions[placementId];
        _dragGroupOriginalPositions.Clear();

        if (finalX == dragOriginal.X && finalY == dragOriginal.Y) return;

        // The drag already moved the view item live for visual feedback — put it back to the start
        // so MoveCommand.Do() is the single place that advances it, keeping undo/redo consistent.
        item.X = dragOriginal.X;
        item.Y = dragOriginal.Y;
        _undoRedo.Execute(new MoveCommand(_session, this, placementId, dragOriginal.X, dragOriginal.Y, finalX, finalY));
        RunAutoConnect(placementId);
        NotifyUndoRedoChanged();
        RedrawRequested?.Invoke();
    }

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

        if (key == Key.Delete && DeleteSelectionCommand.CanExecute(null))
        {
            DeleteSelectionCommand.Execute(null);
            return;
        }

        if (key == Key.R && RotateSelectionCommand.CanExecute(null))
        {
            RotateSelectionCommand.Execute(null);
        }
    }

    private bool CanDeleteSelection() =>
        SelectedDefinitionPointIds.Count > 0 || SelectedCableLineIds.Count > 0 || SelectedPlacementIds.Count > 0 || SelectedPlacementId is not null;

    /// <summary>Deletes whatever's currently selected — any selected definition points (a real delete
    /// now that they're independent entities; the underlying wire, if any was attached, is left
    /// completely untouched — a connection has no independent identity to delete, see ADR-015/
    /// HandleLeftButtonDown's own note) and/or any selected placements, all as one undo step. Reachable
    /// from both the Delete key and the canvas right-click context menu.</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSelection))]
    private void DeleteSelection()
    {
        var commands = new List<IUndoableCommand>();

        foreach (var definitionPointId in SelectedDefinitionPointIds)
        {
            var item = DefinitionPoints.First(p => p.Id == definitionPointId);
            commands.Add(new DeleteDefinitionPointCommand(_session, this, _page.Id, item));
        }

        foreach (var cableLineId in SelectedCableLineIds)
        {
            var item = CableLines.First(c => c.Id == cableLineId);
            commands.Add(new DeleteCableLineCommand(_session, this, _page.Id, item));
        }

        var placementIdsToDelete = SelectedPlacementIds.Count > 1
            ? SelectedPlacementIds.ToList()
            : SelectedPlacementId is { } singleId ? [singleId] : [];
        foreach (var placementId in placementIdsToDelete)
        {
            var item = Placements.First(p => p.PlacementId == placementId);
            commands.Add(new DeleteCommand(_session, this, _page.Id, item, _symbolsByName[item.SymbolName]));
        }

        if (commands.Count == 0) return;

        _undoRedo.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand(commands));
        NotifyUndoRedoChanged();
        SelectedDefinitionPointIds.Clear();
        SelectedCableLineIds.Clear();
        SelectedPlacementIds.Clear();
        SelectedPlacementId = null;
        NotifySelectionCommandsChanged();
        RefreshFromSession();
    }

    private bool CanRotateSelection() =>
        SelectedPlacementId is not null || SelectedDefinitionPointIds.Count == 1 || SelectedCableLineCrossingIds.Count == 1;

    /// <summary>Rotates whatever single item is selected 90° — a placement, a definition point's tick,
    /// or a cable line crossing's tick (each single-select only; a no-op while a multi-selection of that
    /// kind is active). Reachable from both the R key and the canvas right-click context menu.</summary>
    [RelayCommand(CanExecute = nameof(CanRotateSelection))]
    private void RotateSelection()
    {
        if (SelectedPlacementId is { } rotateId)
        {
            var item = Placements.First(p => p.PlacementId == rotateId);
            var newRotation = (item.RotationDegrees + 90) % 360;
            _undoRedo.Execute(new RotateCommand(_session, this, rotateId, item.RotationDegrees, item.Mirrored, newRotation, item.Mirrored));
            RunAutoConnect(rotateId);
            NotifyUndoRedoChanged();
            RedrawRequested?.Invoke();
            return;
        }

        if (SelectedDefinitionPointIds.Count == 1)
        {
            var definitionPointId = SelectedDefinitionPointIds.First();
            var item = DefinitionPoints.First(p => p.Id == definitionPointId);
            var newRotation = (item.RotationDegrees + 90) % 360;
            _undoRedo.Execute(new RotateDefinitionPointCommand(_session, this, definitionPointId, item.RotationDegrees, newRotation));
            NotifyUndoRedoChanged();
            RedrawRequested?.Invoke();
            return;
        }

        if (SelectedCableLineCrossingIds.Count == 1)
        {
            var crossingId = SelectedCableLineCrossingIds.First();
            var (_, crossing) = FindCableLineCrossing(crossingId);
            var newRotation = (crossing.RotationDegrees + 90) % 360;
            _undoRedo.Execute(new RotateCableLineCrossingCommand(_session, this, crossingId, crossing.RotationDegrees, newRotation));
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

    /// <summary>What the renderer should draw as "selected" — the multi-selection if one's active,
    /// otherwise the single selection (if any), otherwise nothing.</summary>
    public IReadOnlyCollection<long> GetEffectiveSelectedPlacementIds() =>
        SelectedPlacementIds.Count > 0 ? SelectedPlacementIds
        : SelectedPlacementId is { } id ? [id]
        : [];

    public RubberBandRenderInfo? BuildRubberBandRenderInfo() =>
        _isRubberBandSelecting
            ? new RubberBandRenderInfo(_rubberBandStartScreen.X, _rubberBandStartScreen.Y, _rubberBandCurrentScreen.X, _rubberBandCurrentScreen.Y)
            : null;

    /// <summary>Everything M7 adds to the render pass — pins, wires, junctions, and the wire-draw preview if one's in progress.</summary>
    public WiringRenderInfo BuildWiringRenderInfo()
    {
        var pinPositions = BuildPinPositions();
        var pinPositionsById = pinPositions.ToDictionary(p => p.DevicePinId, p => p.Position);

        // Whether a drag ends up connected or not is only decided on drop (RunAutoConnect) — stretching
        // an existing wire live to follow the cursor mid-drag would visually imply it's continuously
        // "attached," which it isn't. Hide wires touching the dragged placement's pins for the
        // duration of the drag; they reappear (connected, reconnected, or gone) the moment it's released.
        var draggedPinIds = _dragGroupOriginalPositions.Count > 0
            ? _dragGroupOriginalPositions.Keys
                .SelectMany(id => Placements.FirstOrDefault(p => p.PlacementId == id)?.Pins.Select(p => p.DevicePinId) ?? [])
                .ToHashSet()
            : [];

        var existingConnections = new List<ExistingConnection>();
        var wireRenderInfos = new List<WireRenderInfo>();
        var routesByConnectionId = new Dictionary<long, IReadOnlyList<WorldPoint>>();
        foreach (var connection in Connections)
        {
            if (draggedPinIds.Contains(connection.FromDevicePinId) || draggedPinIds.Contains(connection.ToDevicePinId)) continue;
            if (!pinPositionsById.TryGetValue(connection.FromDevicePinId, out var from)) continue;
            if (!pinPositionsById.TryGetValue(connection.ToDevicePinId, out var to)) continue;

            var route = OrthogonalRouter.Route(from, to);
            existingConnections.Add(new ExistingConnection(connection.ConnectionId, connection.FromDevicePinId, connection.ToDevicePinId, route));
            wireRenderInfos.Add(new WireRenderInfo(connection.ConnectionId, route));
            routesByConnectionId[connection.ConnectionId] = route;
        }

        var junctions = JunctionDetector.FindJunctions(existingConnections, pinPositions);

        IReadOnlyList<WorldPoint>? previewRoute = null;
        if (_wireDrawFromDevicePinId is { } fromPinId && pinPositionsById.TryGetValue(fromPinId, out var fromPos))
            previewRoute = OrthogonalRouter.Route(fromPos, _wireDrawCurrentWorld);
        else if (_drawingCableLineFromWorld is { } cableLineFromWorld)
            previewRoute = [cableLineFromWorld, _cableLineDrawCurrentWorld];

        var definitionPointRenderInfos = DefinitionPoints
            .Select(p => new DefinitionPointRenderInfo(p.Id, p.X, p.Y, p.RotationDegrees, p.WireNumber, p.Color, p.CrossSectionMm2))
            .ToList();

        var cableLineRenderInfos = CableLines
            .Select(c => new CableLineRenderInfo(c.Id, c.X1, c.Y1, c.X2, c.Y2, c.CableTag))
            .ToList();

        // Crossings are re-detected purely for rendering here — a cheap, read-only lookup against each
        // wire's CURRENT route, not a write. If a wire has since moved away and no longer geometrically
        // crosses the line, its tick just doesn't render this frame; the underlying assignment (only
        // ever changed by explicitly drawing/dragging/re-editing the line) is untouched.
        var cableLineCrossingRenderInfos = BuildCableLineCrossingHits(routesByConnectionId)
            .Select(hit => new CableLineCrossingRenderInfo(hit.CrossingId, hit.Position.X, hit.Position.Y, hit.RotationDegrees,
                hit.CableTag, hit.CoreNumber, hit.Color, hit.CrossSectionMm2))
            .ToList();

        return new WiringRenderInfo(
            pinPositions.Select(p => new PinRenderInfo(p.DevicePinId, p.Position)).ToList(),
            wireRenderInfos, definitionPointRenderInfos, SelectedDefinitionPointIds, junctions, previewRoute,
            cableLineRenderInfos, SelectedCableLineIds, cableLineCrossingRenderInfos, SelectedCableLineCrossingIds);
    }

    /// <summary>Every current wire→route mapping, keyed by ConnectionId — the shared lookup
    /// BuildCableLineCrossingHits needs, both for rendering (already has one built inline in
    /// BuildWiringRenderInfo) and for hit-testing/rubber-band outside a render pass.</summary>
    private Dictionary<long, IReadOnlyList<WorldPoint>> BuildRoutesByConnectionId()
    {
        var pinPositions = BuildPinPositions();
        var pinPositionsById = pinPositions.ToDictionary(p => p.DevicePinId, p => p.Position);
        var routes = new Dictionary<long, IReadOnlyList<WorldPoint>>();
        foreach (var connection in Connections)
        {
            if (!pinPositionsById.TryGetValue(connection.FromDevicePinId, out var from)) continue;
            if (!pinPositionsById.TryGetValue(connection.ToDevicePinId, out var to)) continue;
            routes[connection.ConnectionId] = OrthogonalRouter.Route(from, to);
        }
        return routes;
    }

    /// <summary>Every cable line crossing that currently, geometrically resolves to a point on its
    /// wire's live route — the one shared computation used by rendering, click hit-testing, and
    /// rubber-band selection, so the three never disagree about where a crossing's tick actually is.</summary>
    private List<(long CrossingId, WorldPoint Position, int RotationDegrees, string CableTag, int CoreNumber, string? Color, double? CrossSectionMm2)>
        BuildCableLineCrossingHits(IReadOnlyDictionary<long, IReadOnlyList<WorldPoint>> routesByConnectionId)
    {
        var hits = new List<(long, WorldPoint, int, string, int, string?, double?)>();
        foreach (var cableLine in CableLines)
        {
            foreach (var crossing in cableLine.Crossings)
            {
                if (crossing.ConnectionId is not { } id) continue;
                if (!routesByConnectionId.TryGetValue(id, out var route)) continue;
                var point = SegmentIntersection.IntersectRoute(new WorldPoint(cableLine.X1, cableLine.Y1), new WorldPoint(cableLine.X2, cableLine.Y2), route);
                if (point is { } hit)
                    hits.Add((crossing.Id, hit, crossing.RotationDegrees, cableLine.CableTag, crossing.CoreNumber, crossing.Color, crossing.CrossSectionMm2));
            }
        }
        return hits;
    }

    /// <summary>Finds an already-drawn cable line crossing's tick within a small screen-space radius of
    /// the click — same DefinitionPointTickHitRadius convention as a wire definition point's own tick.</summary>
    private long? HitTestCableLineCrossingTick(double screenX, double screenY)
    {
        foreach (var hit in BuildCableLineCrossingHits(BuildRoutesByConnectionId()))
        {
            var (tickScreenX, tickScreenY) = Viewport.WorldToScreen(hit.Position.X, hit.Position.Y);
            var dx = tickScreenX - screenX;
            var dy = tickScreenY - screenY;
            if (Math.Sqrt(dx * dx + dy * dy) <= DefinitionPointTickHitRadius) return hit.CrossingId;
        }
        return null;
    }

    /// <summary>Every cable line crossing whose current tick position falls inside the given
    /// world-space rectangle — the rubber-band's crossing query, same shape as HitTestDefinitionPointsInRect.</summary>
    private List<long> HitTestCableLineCrossingsInRect(double minX, double minY, double maxX, double maxY)
    {
        var hits = new List<long>();
        foreach (var hit in BuildCableLineCrossingHits(BuildRoutesByConnectionId()))
        {
            if (hit.Position.X >= minX && hit.Position.X <= maxX && hit.Position.Y >= minY && hit.Position.Y <= maxY)
                hits.Add(hit.CrossingId);
        }
        return hits;
    }

    /// <summary>Finds which CableLineViewItem owns a given crossing id, and the crossing itself.</summary>
    private (CableLineViewItem Line, CableLineCrossingViewItem Crossing) FindCableLineCrossing(long crossingId)
    {
        foreach (var line in CableLines)
        {
            var crossing = line.Crossings.FirstOrDefault(c => c.Id == crossingId);
            if (crossing is not null) return (line, crossing);
        }
        throw new InvalidOperationException($"CableLineCrossing {crossingId} not found.");
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
        _pendingCenterPlacementId = placementId;
        RedrawRequested?.Invoke();
    }

    /// <summary>A focus request (FocusPlacement, or the ctor's initial focusPlacementId) can't center
    /// the viewport immediately — CanvasViewport has no stored surface pixel size, only the View's
    /// OnPaintSurface sees that, once per frame. Stashed here and resolved by ApplyPendingCenter on
    /// the next paint, once the surface size is actually known.</summary>
    private long? _pendingCenterPlacementId;

    /// <summary>Called by SchematicPageView.xaml.cs right before each Render — pans (never zooms) so a
    /// just-focused placement sits at the center of the current view. Centers on the placement's
    /// stored X/Y (its un-rotated anchor corner, not its true visual center — picture bounds aren't
    /// stored per-placement); close enough to bring it into view, not meant to be pixel-exact.</summary>
    internal void ApplyPendingCenter(double surfaceWidth, double surfaceHeight)
    {
        if (_pendingCenterPlacementId is not { } id) return;
        _pendingCenterPlacementId = null;

        var placement = Placements.FirstOrDefault(p => p.PlacementId == id);
        if (placement is null) return;

        Viewport.PanX = surfaceWidth / (2 * Viewport.Zoom) - placement.X;
        Viewport.PanY = surfaceHeight / (2 * Viewport.Zoom) - placement.Y;
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

    internal void AddConnectionToView(long connectionId, long fromDevicePinId, long toDevicePinId)
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
        });
    }

    internal void RemoveConnectionFromView(long connectionId)
    {
        var item = Connections.FirstOrDefault(c => c.ConnectionId == connectionId);
        if (item is not null) Connections.Remove(item);
    }

    /// <summary>Called when a Connection is deleted — nulls out ConnectionId on any in-memory
    /// DefinitionPointViewItem that referenced it, mirroring the database's own ON DELETE SET NULL for
    /// DefinitionPoint.ConnectionId. The point survives, unattached, exactly where it was; this is the
    /// one hook that makes a definition point actually outlive its wire being deleted/recreated by
    /// auto-connect on the view-model side.</summary>
    internal void DetachDefinitionPointsFromConnectionView(long connectionId)
    {
        foreach (var point in DefinitionPoints.Where(p => p.ConnectionId == connectionId))
            point.ConnectionId = null;
    }

    /// <summary>Fired by ProjectSession.ConnectionsChanged (M7's analog of OnSessionPlacementsChanged) —
    /// keeps this page's wires live across every other open SchematicPageWindow too. Also reloads
    /// definition points, since a connection change can affect an attached one's mirrored data or
    /// attachment (e.g. the connection being deleted detaches it).</summary>
    private void OnSessionConnectionsChanged()
    {
        ReloadConnectionsFromSession();
        ReloadDefinitionPointsFromSession();
        ReloadCableLinesFromSession();
    }

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
            });
        }
        RedrawRequested?.Invoke();
    }

    internal void AddDefinitionPointToView(long definitionPointId, double x, double y, string? wireNumber, string? color,
        double? crossSectionMm2, long? connectionId, int rotationDegrees = 0)
    {
        // Same idempotency guard as AddConnectionToView — ProjectSession.PlaceDefinitionPoint raises
        // DefinitionPointsChanged synchronously, which this ViewModel handles by reloading this page's
        // definition points from the DB before a command's Do() call reaches this line.
        if (DefinitionPoints.Any(p => p.Id == definitionPointId)) return;

        DefinitionPoints.Add(new DefinitionPointViewItem
        {
            Id = definitionPointId,
            X = x,
            Y = y,
            WireNumber = wireNumber,
            Color = color,
            CrossSectionMm2 = crossSectionMm2,
            ConnectionId = connectionId,
            RotationDegrees = rotationDegrees,
        });
    }

    internal void UpdateDefinitionPointRotation(long definitionPointId, int rotationDegrees)
    {
        var item = DefinitionPoints.First(p => p.Id == definitionPointId);
        item.RotationDegrees = rotationDegrees;
    }

    internal void RemoveDefinitionPointFromView(long definitionPointId)
    {
        var item = DefinitionPoints.FirstOrDefault(p => p.Id == definitionPointId);
        if (item is not null) DefinitionPoints.Remove(item);
    }

    internal void UpdateDefinitionPointPosition(long definitionPointId, double x, double y, long? connectionId)
    {
        var item = DefinitionPoints.First(p => p.Id == definitionPointId);
        item.X = x;
        item.Y = y;
        item.ConnectionId = connectionId;
    }

    internal void UpdateDefinitionPointData(long definitionPointId, string? wireNumber, string? color, double? crossSectionMm2)
    {
        var item = DefinitionPoints.First(p => p.Id == definitionPointId);
        item.WireNumber = wireNumber;
        item.Color = color;
        item.CrossSectionMm2 = crossSectionMm2;
    }

    /// <summary>Fired by ProjectSession.DefinitionPointsChanged — keeps this page's definition points
    /// live across every other open SchematicPageWindow too, same cross-window sync pattern as
    /// OnSessionConnectionsChanged.</summary>
    private void OnSessionDefinitionPointsChanged() => ReloadDefinitionPointsFromSession();

    internal void ReloadDefinitionPointsFromSession()
    {
        DefinitionPoints.Clear();
        foreach (var point in _session.GetDefinitionPoints(_page.Id))
        {
            DefinitionPoints.Add(new DefinitionPointViewItem
            {
                Id = point.Id,
                X = point.X,
                Y = point.Y,
                WireNumber = point.WireNumber,
                Color = point.Color,
                CrossSectionMm2 = point.CrossSectionMm2,
                ConnectionId = point.ConnectionId,
                RotationDegrees = point.RotationDegrees,
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

    internal void AddCableLineToView(long cableLineId, double x1, double y1, double x2, double y2, long cableId, string cableTag)
    {
        // Same idempotency guard as AddConnectionToView/AddDefinitionPointToView — ProjectSession's
        // Draw/Move/Reassign methods all raise CableLinesChanged synchronously, which this ViewModel
        // handles by reloading before a command's Do() call reaches this line.
        if (CableLines.Any(c => c.Id == cableLineId)) return;

        var item = new CableLineViewItem { Id = cableLineId, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, CableId = cableId, CableTag = cableTag };
        RefreshCableLineCrossings(item);
        CableLines.Add(item);
    }

    internal void RemoveCableLineFromView(long cableLineId)
    {
        var item = CableLines.FirstOrDefault(c => c.Id == cableLineId);
        if (item is not null) CableLines.Remove(item);
    }

    internal void UpdateCableLineGeometry(long cableLineId, double x1, double y1, double x2, double y2)
    {
        var item = CableLines.First(c => c.Id == cableLineId);
        item.X1 = x1;
        item.Y1 = y1;
        item.X2 = x2;
        item.Y2 = y2;
        RefreshCableLineCrossings(item);
    }

    internal void UpdateCableLineCable(long cableLineId, long cableId, string cableTag)
    {
        var item = CableLines.First(c => c.Id == cableLineId);
        item.CableId = cableId;
        item.CableTag = cableTag;
        RefreshCableLineCrossings(item);
    }

    internal void UpdateCableLineCrossingRotation(long crossingId, int rotationDegrees)
    {
        var (_, crossing) = FindCableLineCrossing(crossingId);
        crossing.RotationDegrees = rotationDegrees;
    }

    internal void UpdateCableLineCrossingCore(long crossingId, int coreNumber, string? color, double? crossSectionMm2)
    {
        var (_, crossing) = FindCableLineCrossing(crossingId);
        crossing.CoreNumber = coreNumber;
        crossing.Color = color;
        crossing.CrossSectionMm2 = crossSectionMm2;
    }

    /// <summary>Re-fetches a cable line's live crossing set (ConnectionId, resolved CoreNumber) from the
    /// database — called whenever its geometry or cable changes, since either can add/re-home crossings.</summary>
    private void RefreshCableLineCrossings(CableLineViewItem item)
    {
        var coresById = _session.GetCableCores(item.CableId).ToDictionary(c => c.Id);
        item.Crossings = _session.GetCableLineCrossings(item.Id)
            .Select(c =>
            {
                coresById.TryGetValue(c.CableCoreId, out var core);
                return new CableLineCrossingViewItem
                {
                    Id = c.Id,
                    ConnectionId = c.ConnectionId,
                    CableCoreId = c.CableCoreId,
                    CoreNumber = core?.CoreNumber ?? 0,
                    Color = core?.Color,
                    CrossSectionMm2 = core?.CrossSectionMm2,
                    RotationDegrees = c.RotationDegrees,
                };
            })
            .ToList();
    }

    /// <summary>Fired by ProjectSession.CableLinesChanged — keeps this page's cable lines live across
    /// every other open SchematicPageWindow too, same cross-window sync pattern as the other *Changed
    /// events.</summary>
    private void OnSessionCableLinesChanged() => ReloadCableLinesFromSession();

    /// <summary>The wire line color, read live from Settings > Preferences (default red) — resolved
    /// fresh on every repaint rather than cached, so a change in the Settings dialog is visible on this
    /// page's very next paint without needing to reopen it.</summary>
    public SKColor WireColor
    {
        get
        {
            try
            {
                return SKColor.Parse(AppSettingsStore.Current.WireColorHex);
            }
            catch
            {
                return SKColor.Parse("#FF0000");
            }
        }
    }

    private void OnAppSettingsChanged() => RedrawRequested?.Invoke();

    internal void ReloadCableLinesFromSession()
    {
        CableLines.Clear();
        foreach (var line in _session.GetCableLines(_page.Id))
        {
            var item = new CableLineViewItem
            {
                Id = line.Id,
                X1 = line.X1,
                Y1 = line.Y1,
                X2 = line.X2,
                Y2 = line.Y2,
                CableId = line.CableId,
                CableTag = _session.GetCable(line.CableId)?.Tag ?? string.Empty,
            };
            RefreshCableLineCrossings(item);
            CableLines.Add(item);
        }
        RedrawRequested?.Invoke();
    }

    public void Dispose()
    {
        _session.PlacementsChanged -= OnSessionPlacementsChanged;
        _session.ConnectionsChanged -= OnSessionConnectionsChanged;
        _session.DefinitionPointsChanged -= OnSessionDefinitionPointsChanged;
        _session.CableLinesChanged -= OnSessionCableLinesChanged;
        AppSettingsStore.SettingsChanged -= OnAppSettingsChanged;
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
