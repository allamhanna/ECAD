# Project Structure

This file describes the actual folder/solution layout as it exists, kept in sync as the project is built. See `DECISIONS.md` ADR-001 for the stack rationale and `PROGRESS.md` for what's built vs planned.

## Current layout (as of M11 — connection definition points + cable definition lines, complete)

```
electrical CAD/
  REQUIREMENTS.md          original requirements doc
  DECISIONS.md             architecture decision log (ADRs)
  PROGRESS.md              milestone checklist + running log
  STRUCTURE.md             this file
  .gitignore
  EcadApp.sln
  src/
    Ecad.App/              WPF startup project (net8.0-windows)
      App.xaml.cs           global DispatcherUnhandledException handler (shows a message box)
      Canvas/                PlacementViewItem (mutable live-canvas placement: position/rotation/tag/
                             Function/Location/SiblingPageLabels/Pins + cached SKPicture), ConnectionViewItem
                             (M7: just a wire's three identity fields — ConnectionId/FromDevicePinId/
                             ToDevicePinId, no cached geometry since its route is recomputed live from
                             current pin positions every render; M11/ADR-018 removed its WireNumber/Color/
                             CrossSectionMm2/DefinitionPointPositionT fields entirely — that data now lives
                             on the independent DefinitionPointViewItem below, not mirrored back here),
                             DefinitionPointViewItem (M11/ADR-018: an independent, symbol-like canvas
                             entity — Id/X/Y/WireNumber/Color/CrossSectionMm2/ConnectionId?/
                             RotationDegrees, mutable in place for live drag/rotate — ConnectionId is
                             optional and non-load-bearing for its own existence), CableLineViewItem +
                             CableLineCrossingViewItem (M11/ADR-019: a cable definition line's own
                             absolute X1,Y1,X2,Y2/CableId/CableTag, and its List<CableLineCrossingViewItem>
                             — each crossing's own Id/ConnectionId?/CableCoreId/CoreNumber/Color/
                             CrossSectionMm2/RotationDegrees, independently selectable/rotatable/editable
                             from the line itself even though its position is resolved live rather than
                             stored), Commands.cs (IUndoableCommand implementations: PlaceSymbolCommand,
                             AttachPlacementCommand (M6: place on an existing Device), MoveCommand,
                             RotateCommand, RenameTagCommand (M6: full Function/Location/Designation),
                             DeleteCommand — see ADR-007/ADR-008 for the undo-of-delete recreate design,
                             now branching on whether the Device survived; CreateConnectionCommand,
                             DeleteConnectionCommand (M11/ADR-018: no longer snapshots/restores a
                             definition point at all — it survives its connection's delete independently
                             now), RenumberWiresCommand (M7, see ADR-009 for the recreate-not-restore/
                             wire-numbering design, reworked in M11 to renumber DefinitionPoint rows
                             instead of Connection.WireNumber directly); CompositeCommand
                             (M10: wraps several IUndoableCommands so a multi-select group move/delete
                             undoes/redoes as one atomic step — Undo runs the wrapped list in reverse, see
                             ADR-016); M11/ADR-018 definition-point commands — PlaceDefinitionPointCommand,
                             MoveDefinitionPointCommand (position + attach/detach in one before/after
                             pair), SetDefinitionPointDataCommand, DeleteDefinitionPointCommand,
                             RotateDefinitionPointCommand (superseded WireNumberDialog/
                             RenameWireNumberCommand, both deleted outright); M11/ADR-019 cable-line
                             commands — DrawCableLineCommand, MoveCableLineCommand,
                             ReassignCableLineCommand, DeleteCableLineCommand,
                             RotateCableLineCrossingCommand, SetCableLineCrossingCoreCommand)
      ViewModels/           MainViewModel (CommunityToolkit.Mvvm ObservableObject + RelayCommands:
                             NewProject, OpenProject, Save, SaveAs, CloseProject, AddPage,
                             ImportEplanPartsAsync, OpenGridEditor, OpenPartsLibrary,
                             OpenSymbolBrowser, Exit; M10/ADR-016: OpenTabs
                             (ObservableCollection<DocumentTabViewModel>) + SelectedTab replace the old
                             per-document Window-launching — OpenPage(Page) now calls
                             OpenOrFocusPageTab(pageId, focusPlacementId), the single find-or-create-tab
                             entry point both MainWindow's double-click-a-page and
                             SchematicPageViewModel.NavigateToPageRequested route through;
                             OpenGridEditor/OpenPartsLibrary/OpenSymbolBrowser each find-or-create their
                             own singleton tab via OpenTabs.FirstOrDefault(t => t.Content is
                             TViewModel) instead of a per-type window registry; CloseTabCommand disposes
                             a tab's Content if IDisposable; OpenProject's body split into
                             OpenProjectFromPath(path) (shared with auto-reopen); TryAutoReopenLastProject
                             (called once from MainWindow's Loaded, not the constructor — see
                             AppSettingsStore below and ADR-016) checks WasExplicitlyClosed/File.Exists
                             before reopening the last project and its first page)
                             DocumentTabViewModel (M10) — Header/Content (the actual per-document
                             ViewModel instance)/IsProjectScoped (controls whether CloseCurrentSession
                             closes this tab too)/PageId (schematic-page tabs' find-existing key); a
                             tab's Content is resolved to its View by implicit DataTemplates declared as
                             MainWindow.xaml resources, keyed by ViewModel type
                             PartsLibraryViewModel — owns its own Library DB connection, search/filter
                             over all Parts + detail lookups (PinTemplates/TerminalSpecs/Accessories/
                             PreviewImage, the last built as a frozen BitmapImage from the cached BLOB)
                             GridEditorViewModel (M8, M9 added TerminationsGridViewModel as a 4th
                             tab) — composition root for the Grid Editor window: owns
                             DevicesGridViewModel/ConnectionsGridViewModel/CablesGridViewModel/
                             TerminationsGridViewModel, subscribes once to
                             PlacementsChanged/ConnectionsChanged/CablesChanged and fans out to
                             whichever tab(s) hold derived data
                             DevicesGridViewModel (M8, edit-only per ADR-011 — no Device creation
                             here, only placing a symbol on a page creates one) — Devices tab: edit
                             Function/Location/Tag inline, bulk-assign a Part across a multi-selection
                             (the ONLY way to assign a Part — see ADR-012: a two-way-bound per-row
                             combo can't reconcile the Library Part.Id used for picking against the
                             project-local Part.Id actually stored on Device.PartId, so the grid
                             works off a DeviceRow display shape — Id/Function/Location/Tag/PartId/
                             PartExternalKey, following PlacementWithSymbol's flattened-read-shape
                             precedent — with the Part column read-only, showing PartExternalKey);
                             owns its own Library DB connection for the read-only Part combo (same
                             pattern as PartsLibraryViewModel). Delete Selected always cascade-deletes
                             (ProjectSession.DeleteDeviceCascade, ADR-015) — Devices are parts with
                             independent identity, so grid-deleting one removes it and every symbol
                             placed for it, on every page, not just a data-only row
                             ConnectionsGridViewModel (M8, edit-only per ADR-011 — no Connection
                             creation here, only drawing/auto-connecting on a page creates one; per
                             ADR-015, also no Connection deletion here — a Connection has no identity
                             independent of the two placements' geometry that produces it, so the
                             only way to remove one is to change that geometry on the canvas) —
                             Connections tab: edit from/to pin (rewiring, via DevicePinOption
                             pickers), color/cross-section/length/Cable/CableCore inline, bulk-set
                             color or cross-section; CoresByCableId dictionary backs the
                             per-row-filtered CableCore picker. M11/ADR-018/019: ConnectionIdsWithDefinitionPoint
                             and ConnectionIdsWithCableLineCrossing (two parallel HashSet<long>, refreshed
                             alongside Connections) make Color/Cross-section read-only for a row whose
                             connection has an attached definition point or a cable-line crossing — "set
                             via canvas," the same treatment WireNumber already had — CommitConnectionEdit
                             reverts (via Refresh()) rather than persisting an edit to either guarded column
                             CablesGridViewModel (M8) — Cables tab: create/edit Cables and their
                             CableCore rows (master-detail), add/delete cores for the selected cable
                             TerminationsGridViewModel (M9) — Terminations tab: every ConnectionEnd
                             in the project (two per Connection, From/To), with the parent
                             Connection's WireNumber/CrossSectionMm2 for read-only context and
                             filtering; inline-editable TerminationEnabled/TerminationType/
                             StrippingLengthMm; termination Part assignment is bulk-toolbar-only via
                             EnsurePartCached (ADR-012, reused as-is), same read-only-
                             TerminationPartExternalKey-column split as Device.PartId. No
                             create/delete here — ConnectionEnd rows are always exactly two per
                             Connection, made together by CreateConnection, cascade-deleted with it.
                             Filtering (cross-section, termination type, unassigned-only) is
                             in-memory over AllRows→FilteredRows, not a re-query. Owns its own
                             Library DB connection for the read-only Part combo (same pattern as
                             DevicesGridViewModel/PartsLibraryViewModel)
                             SchematicPageViewModel — owns a page's CanvasViewport/Placements/Connections/
                             UndoRedoStack; translates raw mouse/keyboard input (reported by
                             SchematicPageView's code-behind, M10) into IUndoableCommand executions;
                             caches loaded SKSvg per symbol name (IDisposable — see ADR-007);
                             subscribes to ProjectSession.PlacementsChanged/ConnectionsChanged (M6/M7,
                             ADR-008/ADR-009) so this page's tags/cross-references/wires stay live even
                             when another open tab's page is what actually changed. M7 additions:
                             pin hit-testing ahead of placement hit-testing, a wire-draw drag state
                             parallel to the existing placement-drag/pan state, direction-aware magnetic
                             pin-snap while placing/dragging (ApplyPinMagnetSnap), and RunAutoConnect
                             (auto-connect *and* auto-disconnect a moved/rotated placement's wiring
                             against its current geometry every time — see ADR-009). Interruption-points
                             follow-up: Ctrl+Click support in HandleLeftButtonDown and a
                             NavigateToPageRequested event (siblingPageId, siblingPlacementId) — fires
                             for any placement with a sibling, not just interruption points; optional
                             focusPlacementId constructor parameter selects that placement once loaded.
                             M10/ADR-016 additions: GridSpacing (+ GridSpacingOptions 10/20/40/80,
                             two-way bound to Viewport.GridSpacing, only ever affects rendering/snap
                             density — see SchematicCanvasRenderer's grid-as-dots note below);
                             PlaceSymbolAtScreenPosition (drag-and-drop from the palette, thin wrapper
                             over the existing click-to-place PlaceSymbolAt); rubber-band multi-select —
                             SelectedPlacementIds (HashSet<long>, populated only when more than one
                             placement is selected together), HandlePanStart/HandlePanEnd (moved to
                             middle-click-drag), a unified _dragGroupOriginalPositions dictionary so a
                             single-item drag is just the Count==1 case of the same group-drag mechanism
                             (group drags snap the shared delta as one, preserving relative offsets;
                             magnetic pin-snap stays single-item-only), GetEffectiveSelectedPlacementIds/
                             BuildRubberBandRenderInfo feeding SchematicCanvasRenderer.Render; Delete and
                             drag-move both build one command per affected placement and wrap them in a
                             CompositeCommand when more than one is selected — Rotate (R) and wire/
                             connection selection stay single-select-only. M10/ADR-017 refinements:
                             left-click-drag on empty space is the rubber-band marquee itself (an
                             _isRubberBandArmed/_isRubberBandSelecting handshake across
                             HandleLeftButtonDown/HandleMouseMove/HandleLeftButtonUp: armed immediately,
                             only becomes an active marquee once the drag crosses RubberBandDragThreshold,
                             so a stationary click still selects a wire/deselects exactly as before — a
                             rubber-band hit-test only checks placements, so it would otherwise never see
                             a wire click); the marquee is direction-aware (FinishRubberBandSelection —
                             left-to-right requires a placement be fully enclosed via
                             HitTestRect's requireFullContainment, right-to-left only requires touching);
                             CaptureMouse/ReleaseMouseCapture on every left/middle-button drag (so
                             dragging keeps tracking past the canvas's edge) plus CancelActiveDrag
                             (reverts a placement drag to its pre-drag position, resets all drag flags) —
                             wired to the SKElement's LostMouseCapture, a defensive fallback if capture is
                             ever taken away mid-drag. Right-click (HandleRightClick) selects whatever's
                             under the cursor without starting a drag, then a DeleteSelectionCommand/
                             RotateSelectionCommand pair (extracted out of HandleKeyDown's Delete/R
                             branches so both the keyboard and a context menu share one implementation and
                             enabled state) backs SchematicPageView's ContextMenu (Rotate/Delete/Undo/
                             Redo). M10 Phase 4 (docked device panel, ADR-017): IsDevicePanelOpen,
                             DevicePanelFunction/Location/Designation (live DeviceTag preview recomputed
                             every keystroke via partial void On*Changed hooks), DevicePanelValidationText,
                             ApplyDeviceEditCommand/CloseDevicePanelCommand — HandleDoubleClick's
                             rename-an-existing-placement branch populates this state instead of
                             constructing/showing a DeviceTagDialog; new-placement's tag prompt
                             (PlaceSymbolAt) is untouched (M11/ADR-018 replaced the old double-click-a-
                             wire-to-rename-its-number flow entirely — see below).
                             M11/ADR-018 (connection definition points) additions: IsPlacingDefinitionPoint
                             toolbar-toggle bool (mutually exclusive with SelectedPaletteSymbol/
                             IsDrawingCableLine below); DefinitionPoints (ObservableCollection<
                             DefinitionPointViewItem>) + SelectedDefinitionPointIds (HashSet<long>, keyed
                             by the point's own Id, populated by click/right-click/rubber-band);
                             HandleLeftButtonDown's hit-test order is pin -> definition-point tick
                             (HitTestDefinitionPointTick, fixed screen-pixel radius) -> cable-line-crossing
                             tick -> cable-line endpoint -> cable-line body -> placement -> rubber-band-arm;
                             dragging an existing point live-mutates its X/Y (grid-snapped) the same way a
                             placement drag does, re-testing WireHitTester.HitTestWireForDefinitionPoint on
                             drop to decide attach/detach/switch (FinishDefinitionPointDrag); double-click
                             opens DefinitionPointDialog (edit existing) or, via the toolbar tool
                             (TryPlaceDefinitionPointAt), places a brand-new one — snapped onto a nearby
                             wire if one's close, or dropped free-floating otherwise; RotateSelection was
                             widened from placement-only to also rotate a single selected definition point
                             or cable-line crossing (see below) 90° via R.
                             M11/ADR-019 (cable definition lines) additions: IsDrawingCableLine
                             toolbar-toggle bool; CableLines (ObservableCollection<CableLineViewItem>) +
                             SelectedCableLineIds/SelectedCableLineCrossingIds; mousedown-anywhere while
                             the tool is active starts a live dashed-preview drag (reusing the existing
                             wire-draw-preview rendering plumbing), mouseup finalizes the second endpoint,
                             runs DetectCableLineCrossings (a pure SegmentIntersection.IntersectRoute test
                             against every current wire route) and — only if at least one wire was
                             crossed — opens CableLineDialog (a single Cable Tag field, pre-filled via
                             ProjectSession.SuggestNextCableTag); dragging an existing line's body
                             translates both endpoints together (grid-snapped via a snapped-anchor-then-
                             derived-delta trick, matching the placement-group-drag convention — a real
                             grid-snap bug was found and fixed here), while dragging either endpoint alone
                             extends/shrinks it; double-click re-opens the dialog (changing the tag
                             re-homes every live crossing to the found-or-created new Cable) or removes
                             the line entirely. A crossing's own tick (HitTestCableLineCrossingTick,
                             BuildCableLineCrossingHits — the one shared live-position computation used by
                             rendering, click hit-testing, and rubber-band alike) is independently
                             selectable/rotatable and double-click-editable via CableCoreDialog (core
                             number/color/cross-section, with a uniqueness check)
      Services/              AppSettingsStore (M10/ADR-016) — Load/Save a small JSON file at
                             %LOCALAPPDATA%\Ecad\settings.json (mirrors LibraryDatabase's path idiom):
                             LastOpenedProjectPath + WasExplicitlyClosed, read by
                             MainViewModel.TryAutoReopenLastProject on MainWindow's Loaded event
      Views/                NewProjectDialog, AddPageDialog — plain code-behind modal dialogs
                             PartsLibraryView (M10: extracted from the old PartsLibraryWindow, now a
                             UserControl hosted as a singleton, project-independent tab) — master-detail
                             parts browser (read-only), detail panel includes an Image control bound to
                             PreviewImage; its RelativeSource bindings became AncestorType=UserControl
                             (were AncestorType=Window)
                             SymbolBrowserViewModel — loads SymbolLibrary/ via SymbolLibraryLoader,
                             rasterizes each via SymbolRasterizer, exposes thumbnails
                             SymbolBrowserView (M10: extracted from the old SymbolBrowserWindow, same
                             singleton-tab treatment as PartsLibraryView) — wrapped thumbnail grid
                             (read-only)
                             DeviceTagDialog — segment-aware IEC 81346 tag editor (M6): Function/
                             Location/Designation fields with a live tag preview and uniqueness
                             validation, plus a device picker ("New Device" or an existing one to attach
                             to) for the new-placement tag prompt. Since M10 Phase 4 (ADR-017), this is
                             its only remaining mode — the rename-mode constructor (and the now-always-
                             null _excludingDeviceId field it was the sole source of) was deleted once
                             SchematicPageView's docked device panel (see SchematicPageViewModel's
                             DevicePanel* state above) took over renaming an existing placement, per this
                             project's "delete what's actually unused" convention (ADR-011/ADR-015)
                             WireNumberDialog — deleted in M11/ADR-018, superseded by DefinitionPointDialog
                             (Wire#/Cross-section/Color + Remove, shown both when placing a brand-new
                             definition point and when double-clicking an existing one to edit/remove it)
                             CableLineDialog (M11/ADR-019) — minimal single-field Cable Tag dialog, shown
                             both when finalizing a brand-new cable line (Remove hidden) and when
                             double-clicking an existing one (Remove shown, re-homes on a changed tag)
                             CableCoreDialog (M11/ADR-019) — edits one cable-line crossing's own
                             CableCore directly (core number, cross-section, color), with a uniqueness
                             check on the core number against the cable's other cores
                             SchematicPageView (M10/ADR-016: extracted from the old SchematicPageWindow,
                             a UserControl now hosted as a tab in MainWindow's document TabControl
                             instead of a floating per-page Window; the old static
                             Dictionary<long, SchematicPageWindow> page registry and OpenOrFocus are
                             gone — MainViewModel.OpenOrFocusPageTab is the one "show me this page" entry
                             point now) — SKElement (first interactive use of SkiaSharp.Views.WPF,
                             IgnorePixelScaling="True" per ADR-007) + a palette sidebar of the M4 starter
                             symbols (drag-and-drop onto the canvas as well as click-to-place, M10) + a
                             grid-spacing combo + a "Renumber Wires" toolbar button (M7) + "Place
                             Definition Point"/"Draw Cable Line" toggle buttons (M11/ADR-018/019); mouse/keyboard
                             events are translated to SchematicPageViewModel calls, nothing else lives in
                             the code-behind. A DataContextChanged handler (not the constructor) wires
                             RedrawRequested and forces an InvalidateVisual, with a second
                             InvalidateVisual on Loaded — implicit-DataTemplate-hosted Views get their
                             DataContext assigned after construction, and SKElement doesn't reliably
                             self-paint the first time it's freshly templated into a tab; see ADR-016 for
                             the real bug this fixed (a newly-opened tab stayed blank until a stray
                             click). Mouse buttons: left = select/drag/place, and left-drag on empty
                             space is the rubber-band multi-select (armed on button-down, only becomes an
                             active marquee once the drag crosses a small pixel threshold, so a
                             stationary click still selects a wire/deselects exactly as before, and is
                             direction-aware — see PlacementHitTester.HitTestRect below); right-
                             click selects whatever's under the cursor and opens a ContextMenu (Rotate/
                             Delete/Undo/Redo, bound to DeleteSelectionCommand/RotateSelectionCommand —
                             shared with the Delete/R keyboard shortcuts, the ContextMenu's DataContext
                             re-bound via PlacementTarget.DataContext since a ContextMenu is a separate
                             popup that doesn't inherit it automatically); middle-drag = pan; wheel = zoom.
                             SkiaCanvas.CaptureMouse() on every left/middle button-down (released on the
                             matching button-up) keeps a drag tracking correctly even once the cursor
                             leaves the canvas's bounds — without it WPF stops delivering MouseMove/
                             MouseUp the instant the cursor exits, freezing the drag (ADR-017); a
                             LostMouseCapture handler calls SchematicPageViewModel.CancelActiveDrag as a
                             defensive fallback if capture is ever taken away mid-drag. A collapsible
                             Border, docked to the canvas's right edge and bound through a
                             BooleanToVisibilityConverter, is the M10 Phase 4 docked device panel —
                             visible only while IsDevicePanelOpen
                             GridEditorView (M8, M9 added a 4th tab; M10/ADR-016: extracted from the old
                             GridEditorWindow into a UserControl, hosted as a singleton project-scoped
                             tab found via OpenTabs.FirstOrDefault(t => t.Content is
                             GridEditorViewModel) instead of a dedicated OpenOrFocus registry) — a
                             TabControl hosting DevicesGridView/ConnectionsGridView/CablesGridView/
                             TerminationsGridView, each DataContext-bound to its own tab view-model.
                             First editable (not read-only) DataGrid usage in the app — row edits are
                             persisted via a RowEditEnding handler that defers to
                             Dispatcher.BeginInvoke so the grid's own binding commits before the
                             view-model reads the edited object. Needs none of SchematicPageView's
                             DataContextChanged/InvalidateVisual workaround — every child control here is
                             an ordinary WPF-bound DataGrid, not a manually-painted SKElement
      SymbolLibrary/         8 starter IEC-style symbols: RelayCoil, ContactNO, ContactNC, Terminal,
                             Motor3Phase, PushbuttonNO, Fuse, Lamp — each a plain .svg + .symbol.json
                             sidecar (see ADR-006), Content/CopyToOutputDirectory. InterruptionPoint (M7
                             follow-up): an arrow with a connection point at *both* ends, so either end
                             can be wired and the arrow reads as pointing away from or into the real
                             connection without needing to rotate the symbol; pairing two placements of
                             it across pages (via the existing "attach to existing Device" flow) is what
                             represents a cross-page wire continuation — no new schema needed
      Assets/AppIcon.ico     placeholder app icon (multi-size .ico, generated programmatically —
                             not hand-designed): a schematic wire/pin/junction motif matching
                             SchematicCanvasRenderer's own visual language. Set via
                             <ApplicationIcon> in Ecad.App.csproj (exe/taskbar icon) and
                             MainWindow.xaml's Icon attribute (title bar/Alt-Tab)
      Converters/            CableCoreOptionsConverter (M8) — IMultiValueConverter used by
                             ConnectionsGridView's CableCoreId column: filters the CableCore list to
                             the row's own CableId, since a plain DataGridComboBoxColumn can't filter
                             its item source per row
      MainWindow.xaml(.cs)  M10/ADR-016: restructured to a single window — a Pages sidebar ListView
                             (Function/Location/DocType/PageNumber/Type columns, double-click a row to
                             open/focus its tab via MainViewModel.OpenPage) beside a document TabControl
                             bound to OpenTabs/SelectedTab, each tab's Content resolved to its View by
                             implicit DataTemplates declared in Window.Resources (keyed by
                             SchematicPageViewModel/GridEditorViewModel/PartsLibraryViewModel/
                             SymbolBrowserViewModel); a tab-header DataTemplate adds a "✕" close button
                             bound to CloseTabCommand. File menu (Grid Editor/Parts Library/Symbol
                             Browser no longer have "..." — they open a tab, not a dialog/window), status bar;
                             DataContext = MainViewModel
    Ecad.Core/             domain models & business logic (net8.0, no UI/storage deps)
      Models/              Project, Page, Device, DevicePin, Placement, PlacementPin, Connection, ConnectionEnd,
                            Cable (+ ProjectId, M8), CableCore, Part, PartPinTemplate, PartTerminalSpec, PartAccessory, PartImage,
                            Organization, Classification, Symbol, ImportBatch, UdpDefinition, UdpValue,
                            PlacementWithSymbol (+ FunctionSegment/LocationSegment/Siblings/Pins, M6/M7),
                            SiblingPlacementRef (PlacementId, PageId, PageLabel — M6 cross-reference display),
                            PlacementPinInfo (DevicePinId, Name — M7, lets the canvas resolve a pin's world
                            position via the symbol's matching-named connection point),
                            ConnectionEndWithContext (M9: ConnectionEnd's own fields + the parent
                            Connection's WireNumber/CrossSectionMm2/FromDevicePinId/ToDevicePinId —
                            the Terminations tab's one join-result read shape, following
                            PlacementWithSymbol's existing "joined read shape" precedent),
                            DefinitionPoint (M11/ADR-018: Id/PageId/X/Y/WireNumber/Color/CrossSectionMm2/
                            ConnectionId?/RotationDegrees — an independent, symbol-like entity, not a
                            Connection field; Connection itself lost DefinitionPointPositionT entirely),
                            CableLine + CableLineCrossing (M11/ADR-019: CableLine is Id/PageId/
                            X1,Y1,X2,Y2/CableId; CableLineCrossing is Id/CableLineId/ConnectionId?/
                            CableCoreId/RotationDegrees — same independent-entity/nullable-ConnectionId
                            shape as DefinitionPoint, for the same "survive an unrelated auto-connect
                            rewrite" reason)
      ValueObjects/         DeviceTag (=Function+Location-Tag), PageTag (=Function+Location&DocType/Page)
      Enums/                PageType, PartType, TerminationType, UdpDataType, UdpEntityType,
                            ConnectionEndDesignator, ImportSourceType
    Ecad.Data/             SQLite access layer (net8.0)
      Migrations/Project/0001_initial.sql   Project DB schema (+ local cached Part tables)
      Migrations/Library/0001_initial.sql   Library DB schema (Part/PartPinTemplate/PartTerminalSpec/
                                             PartAccessory DDL intentionally identical to Project's copy)
      Migrations/{Project,Library}/0002_part_images.sql   PartImage table (BLOB, see ADR-005) —
                                             again identical DDL in both databases
      Migrations/Project/0003_cable_project_id.sql   M8/ADR-010: adds Cable.ProjectId (Project DB
                                             only — Cable/CableCore are real per-project data, not a
                                             cached-Part-family table like the two above)
      Migrations/Project/0004_connection_definition_point.sql   M11/ADR-018: original
                                             Connection.DefinitionPointPositionT column — superseded by
                                             0005 below, kept only for migration-history continuity
      Migrations/Project/0005_definition_point_entity.sql   M11/ADR-018: creates the DefinitionPoint
                                             table (backfilling existing route-relative points as an
                                             approximate absolute position) and drops the now-unused
                                             Connection.DefinitionPointPositionT column
      Migrations/Project/0006_cable_line.sql   M11/ADR-019: creates CableLine + CableLineCrossing
      Migrations/Project/0007_definition_point_rotation.sql   M11: adds RotationDegrees to both
                                             DefinitionPoint and CableLineCrossing
      MigrationRunner.cs    applies embedded .sql files in order, tracks schema_migrations table
      ProjectDatabase.cs    opens/creates a project's single-file SQLite db, runs Project migrations
      LibraryDatabase.cs    opens/creates %LOCALAPPDATA%\Ecad\library.db, runs Library migrations
      ProjectSession.cs     Create/Open a .ecad file, holds CurrentProject + Pages, AddPage, Checkpoint
                             (File > Save), SaveAs (checkpoint + file copy + reopen on new path),
                             Dispose — the testable core behind Ecad.App's MainViewModel; M6 added
                             PlaceSymbolOnExistingDevice, RenameDeviceTag, GetAllDevices,
                             SuggestNextDesignation, IsTagAvailable, a placement-level (not device-level)
                             DeletePlacement returning PlacementDeletionResult, and the
                             PlacementsChanged event (ADR-008) every SchematicPageViewModel subscribes to;
                             M7 added CreateConnection, DeleteConnection, GetConnectionsForPage,
                             AreConnected, IsWireNumberAvailable, SuggestNextWireNumber,
                             RenumberAllWires/ApplyWireNumbers, GetPlacementPins, the ConnectionsChanged
                             event (ADR-009), and fixed DeletePlacement to delete dependent Connections
                             before the pins they reference (no ON DELETE CASCADE on that FK);
                             M8 added UpdateDevicePart/BulkUpdateDevicePart,
                             AddDevicePin/UpdateDevicePin/CanDeleteDevicePin/DeleteDevicePin,
                             GetAllConnections, UpdateConnectionColor/
                             CrossSection/Cable/Endpoints, BulkUpdateConnectionColor/CrossSection,
                             GetAllCables/CreateCable/UpdateCable/CanDeleteCable/DeleteCable,
                             GetCableCores/AddCableCore/UpdateCableCore/DeleteCableCore, and the
                             CablesChanged event (ADR-010, same shape as PlacementsChanged/ConnectionsChanged);
                             ADR-015 replaced the short-lived CanDeleteDevice/DeleteDevice with
                             DeleteDeviceCascade(deviceId) — deletes a Device and every Placement of
                             it across every page (plus dependent Connections), via a private
                             DeletePlacementCore shared with the unchanged public DeletePlacement,
                             raising PlacementsChanged/ConnectionsChanged once each for the whole
                             batch;
                             ADR-012 added a _parts (PartRepository) field — the first ProjectSession
                             field for the Part-family tables — plus EnsurePartCached(libraryPartId,
                             libraryFilePath?) (copies a Library Part, and its Manufacturer/Supplier
                             Organization by ExternalKey, into the project's own Part table, returning
                             the project-local Id Device.PartId's FK actually needs) and GetCachedPart;
                             M9 added GetAllConnectionEndsWithContext, UpdateConnectionEndTermination,
                             BulkUpdateConnectionEndPart (reuses EnsurePartCached as-is for
                             TerminationPartId, same FK requirement as Device.PartId);
                             M11/ADR-018 added a _definitionPoints (DefinitionPointRepository) field and
                             PlaceDefinitionPoint/MoveDefinitionPoint/RotateDefinitionPoint/
                             SetDefinitionPointData/AttachDefinitionPointToConnection/
                             DetachDefinitionPoint/DeleteDefinitionPoint/GetDefinitionPoints/
                             GetConnectionIdsWithDefinitionPoint, the DefinitionPointsChanged event
                             (same shape as PlacementsChanged/ConnectionsChanged/CablesChanged), and
                             reworked RenumberAllWires/SuggestNextWireNumber/IsWireNumberAvailable to
                             operate on DefinitionPoint rows instead of Connection.WireNumber directly;
                             M11/ADR-019 added a _cableLines (CableLineRepository) field and
                             DrawCableLine/MoveCableLine/ReassignCableLine/DeleteCableLine/GetCableLines/
                             GetCableLine/GetCableLineCrossings/GetCableLineCrossing/
                             RotateCableLineCrossing/SetCableLineCrossingCore/IsCableCoreNumberAvailable/
                             SuggestNextCableTag/GetConnectionIdsWithCableLineCrossing, the
                             CableLinesChanged event, and GetCable(cableId) (a singular counterpart to
                             the existing GetAllCables) — all of DrawCableLine/MoveCableLine/
                             ReassignCableLine share a private AssignCrossings helper (auto-numbers a
                             fresh CableCore per newly-detected crossing, skips one already assigned to
                             a different cable, mirrors onto Connection.CableId/CableCoreId via the
                             already-existing UpdateConnectionCable)
      PlacementDeletionResult.cs   M6: what DeletePlacement did (whole Device removed, or just the
                             placement) — tells DeleteCommand.Undo() which recreate strategy to use
      Repositories/         ProjectRepository (+ GetFirstProject, GetPages), DeviceRepository
                             (+ DeleteDevice, UpdateDeviceTag with Function/Location, GetAllDevices,
                             FindByTag, SuggestNextDesignation — M6; UpdateDevicePart, UpdateDevicePin,
                             DeleteDevicePin — M8), PlacementRepository
                             (+ GetOrCreateSymbol, UpdatePosition, UpdateRotation, GetPlacementsForPage,
                             GetSiblingPlacementRefs, GetPlacementPinNames, CountPlacementsForDevice,
                             DeleteExclusiveDevicePinsForPlacement, DeletePlacement — M6; GetPlacementPins
                             — M7; GetPlacementIdsForDevice — ADR-015, backs DeleteDeviceCascade),
                             ConnectionRepository (M7: DeleteConnection, GetConnectionsForPage,
                             GetConnectionsForDevicePin, AreConnected, UpdateWireNumber — was bare
                             Insert/Get only since M1; M8: GetAllConnectionsForProject,
                             UpdateConnectionColor/CrossSection/Cable/Endpoints,
                             AnyConnectionReferencesCable, ClearCableCoreReferences; M9:
                             GetAllConnectionEndsWithContext, UpdateConnectionEndTermination,
                             UpdateConnectionEndPart — InsertConnectionEnd/GetConnectionEnds existed
                             since M1 but nothing had ever updated a ConnectionEnd's termination fields
                             until now; M11/ADR-018 removed SetDefinitionPoint/ClearDefinitionPoint/
                             FindByWireNumber/GetAllWireNumbers/GetConnectionIdsForRenumbering — that
                             data and those queries moved to DefinitionPointRepository below, since
                             wire-number/color/cross-section data no longer lives on Connection at all
                             once a definition point is attached), CableRepository (M8: GetAllCables,
                             UpdateCable, DeleteCable, UpdateCableCore, DeleteCableCore — was bare
                             Insert/Get only since M1, and InsertCable now sets ProjectId; M11/ADR-019
                             added GetCableCore(id), a singular counterpart to GetCableCores(cableId)),
                             PartRepository
                             (+ upsert-by-ExternalKey, Replace* child-row helpers, GetOrCreateOrganization,
                             GetAllParts, GetAllOrganizations, GetImage, UpsertImage; GetOrganization(id) —
                             ADR-012), UdpRepository,
                             DefinitionPointRepository (M11/ADR-018: Insert/Get/GetForPage/
                             GetByConnectionId/UpdatePosition/UpdateData/SetConnection/UpdateRotation/
                             Delete, plus project-wide GetIdsForRenumbering/GetAllWireNumbers/
                             FindByWireNumber/GetAttachedConnectionIds — the wire-number-uniqueness and
                             renumbering queries ConnectionRepository used to own),
                             CableLineRepository (M11/ADR-019: Insert/Get/GetCableLinesForPage/
                             UpdateGeometry/UpdateCableId/Delete for CableLine; InsertCrossing/
                             GetCrossingsForLine/GetCrossing/UpdateCrossingRotation/DeleteCrossing/
                             DeleteCrossingsForLine for CableLineCrossing; GetAttachedConnectionIds for
                             the Grid Editor's read-only guard) —
                             Dapper on top of Microsoft.Data.Sqlite
      Import/EplanEdzImporter.cs   parses a real EPLAN .edz (7z, read via SharpCompress) into the
                             Library DB — see ADR-004 for format quirks this handles, ADR-005 for
                             the unconditional (Added/Updated/Unchanged-independent) image backfill
      Import/EplanImportResult.cs  counts + warnings returned to the caller
    Ecad.Rendering/        canvas + SVG symbol rendering (net8.0-windows, UseWPF)
      Canvas/CanvasViewport.cs         pan/zoom/grid-spacing state + WorldToScreen/ScreenToWorld/SnapToGrid
      Canvas/PlacementHitTester.cs     rotation-aware topmost-hit test over a placement list (HitTest);
                             M10/ADR-016 added HitTestRect (AABB intersection against a world-space
                             rectangle, deliberately NOT rotation-aware — the rubber-band multi-select
                             marquee query, where "roughly overlaps" is what a user expects); ADR-017
                             added an optional requireFullContainment parameter (default false/crossing,
                             matching the original behavior) — true switches to the AutoCAD/EPLAN-style
                             "window select" rule (a placement must be entirely inside the rectangle),
                             which SchematicPageViewModel.FinishRubberBandSelection picks based on the
                             drag's own left-to-right vs. right-to-left screen direction
      Canvas/UndoRedoStack.cs          generic IUndoableCommand push/undo/redo, no WPF/Ecad.Data dependency
      Canvas/SchematicCanvasRenderer.cs  pure SkiaSharp draw: grid (rendered as dots since M10, not
                             lines — GridSpacing only ever moves where dots land/where SnapToGrid rounds
                             to, never an existing Placement's stored world position), wires (drawn
                             before placements, a plain line only — a connection has no independent
                             selectable identity or definition-point data of its own since M11/ADR-018),
                             junction dots, each placement (rotated/mirrored/scaled to its world
                             footprint), selection highlight (Render's selectedPlacementId param became
                             selectedPlacementIds: IReadOnlyCollection<long> in M10, so multiple
                             placements can be marked selected at once), device tag text, sibling
                             cross-reference text (M6, only drawn when siblings exist), pin markers (M7,
                             drawn on top of placements), wire-draw dashed preview (M7, drawn topmost),
                             rubber-band marquee (M10: RubberBandRenderInfo, a dashed translucent-filled
                             rectangle, drawn topmost of all). M11/ADR-018: DefinitionPointRenderInfo is
                             now a top-level, independent record (Id/X/Y/RotationDegrees/WireNumber/
                             Color/CrossSectionMm2) drawn every frame like a placement, not derived from
                             any wire; DrawDefinitionPointGlyph (the shared diagonal-tick-plus-label
                             glyph, red by default/DodgerBlue when selected, rotates via a Save/
                             Translate/RotateDegrees/Restore transform around the tick only — the label
                             stays upright, same convention DrawPlacement's own tag text already uses)
                             is reused as-is by cable line crossings. M11/ADR-019: CableLineRenderInfo
                             (a dashed brownish line between two absolute endpoints) and
                             CableLineCrossingRenderInfo (Id/X/Y/RotationDegrees/CableTag/CoreNumber/
                             Color/CrossSectionMm2 — resolved live by the ViewModel each frame via
                             SegmentIntersection, not stored) drawn via the same DrawDefinitionPointGlyph
                             helper
      Canvas/ConnectionModels.cs       M7: WorldPoint (value-equatable world-space point), PinPosition
                             (DevicePinId + world position + outward Direction), ExistingConnection
                             (endpoints + already-routed path) — shared inputs for the pure classes below
      Canvas/PlacementPinGeometry.cs   M7: transforms a connection point's local (0..40) position/
                             direction by a placement's position/rotation/mirror into world space —
                             the same forward transform SchematicCanvasRenderer applies to the symbol
                             picture, applied to a single point (and, for direction, mirror-then-rotate:
                             180-d then +rotation) instead
      Canvas/OrthogonalRouter.cs       M7: straight-or-one-bend route between two world points — no
                             stored waypoints, recomputed fresh every render (see ADR-009)
      Canvas/AutoConnectDetector.cs    M7: direction-aware "facing on the same grid line" detection
                             (AreFacingEachOther/AreOppositeDirections) plus pin-lands-mid-span-on-an-
                             existing-wire detection — see ADR-009 for why this isn't simple proximity
      Canvas/JunctionDetector.cs       M7: derives junction-dot points from the existing pairwise
                             Connection records — no separate junction entity
      Canvas/WireHitTester.cs          M7: proximity-based pin and wire (point-to-polyline) hit-testing;
                             M11/ADR-018 added HitTestWireForDefinitionPoint (nearest wire + a 0..1
                             length-fraction, via RouteMath.ProjectToT — used to snap a definition-point
                             placement/drag onto a wire)
      Canvas/RouteMath.cs              M11/ADR-018: PointAtT/ProjectToT — arc-length interpolation and
                             nearest-point-as-length-fraction over a route's polyline, the math behind
                             snapping a definition point onto (or projecting off of) a wire's current path
      Canvas/SegmentIntersection.cs    M11/ADR-019: Intersect (parametric 2D segment/segment
                             intersection) + IntersectRoute (tests a straight segment against every
                             segment of a wire's route) — the one genuinely new geometry primitive this
                             codebase needed for cable lines; nothing like a crossing test existed before
                             (RouteMath/WireHitTester are both point-to-polyline proximity only)
      Canvas/CableLineHitTester.cs     M11/ADR-019: point-to-segment proximity hit-testing for an
                             already-drawn cable line, same shape as WireHitTester's own private
                             DistanceToSegment
      Symbols/SymbolDefinition.cs      ConnectionPoint/TextPlaceholder/Variant POCOs + JSON (de)serialization
      Symbols/SymbolLibraryLoader.cs   scans a folder for *.symbol.json + matching .svg (see ADR-006)
      Symbols/SymbolRasterizer.cs      SkiaSharp + Svg.Skia: SVG bytes -> PNG byte array (no WPF dependency)
    Ecad.Reports/          report layout + QuestPDF generation (net8.0) — QuestPDF added, empty stub
  tests/
    Ecad.Core.Tests/       DeviceTagTests, PageTagTests (8 tests)
    Ecad.Data.Tests/       MigrationTests, ProjectSchemaTests, PartUpsertTests, ProjectSessionTests,
                           ProjectSessionPlacementTests (PlaceSymbol/Move/Rotate/Delete, symbol reuse),
                           ProjectSessionMultiPlacementTests (M6: attach-to-existing-device, delete
                           keeps/removes the Device correctly, tag uniqueness, Designation auto-suggest
                           scoping, sibling label round-trip),
                           ProjectSessionConnectionTests (M7: CreateConnection/AreConnected/
                           DeleteConnection, DeletePlacement cleans up dependent Connections; M11/ADR-018
                           rewrote most of this file's definition-point cases as the entity moved off
                           Connection — RotateDefinitionPoint round-trip added),
                           ProjectSessionGridEditingTests (M8: DevicePin add/update/delete-when-wired-
                           throws, project-wide GetAllConnections, endpoint rewire, single/bulk
                           color+cross-section updates, Cable project-scoping across two separate
                           project files, orphan-cable listing, Cable delete-guard, CableCore delete
                           auto-clear, CablesChanged firing, DeleteDeviceCascade across pages — see
                           ADR-010/011/012/015; M9: ConnectionEnd termination field round-trip+clear,
                           GetAllConnectionEndsWithContext's parent-Connection context/project-scoping/
                           both-End-designators, EnsurePartCached reused for a termination part,
                           BulkUpdateConnectionEndPart targeting+once-per-batch+null-clears),
                           ProjectSessionCableLineTests (M11/ADR-019: draw-across-two-connections
                           sequential-core-numbering+mirroring, case-insensitive cable-tag reuse,
                           skip-a-connection-already-on-a-different-cable, idempotent re-draw/move,
                           delete clears mirrors, the delete-the-connection-directly orphaning
                           regression case, re-home to a different cable, SuggestNextCableTag
                           increments, RotateCableLineCrossing, SetCableLineCrossingCore mirrors onto
                           the live connection, IsCableCoreNumberAvailable),
                           EplanEdzImporterTests (synthetic zip fixtures, see ADR-004/ADR-005),
                           TempSqliteFile helper (102 tests)
    Ecad.Rendering.Tests/  SymbolLibraryLoaderTests, SymbolRasterizerTests, CanvasViewportTests,
                           PlacementHitTesterTests (M10 added 4 HitTestRect tests: fully-inside,
                           partial-overlap, excluded, corner-order-independent; ADR-017 added 2 more for
                           requireFullContainment: excludes-a-partial-overlap, includes-full-containment),
                           UndoRedoStackTests, PlacementPinGeometryTests, OrthogonalRouterTests,
                           AutoConnectDetectorTests, JunctionDetectorTests, WireHitTesterTests
                           (M11/ADR-018 added HitTestWireForDefinitionPoint cases), RouteMathTests
                           (M11/ADR-018: PointAtT/ProjectToT over straight and one-bend routes),
                           SegmentIntersectionTests (M11/ADR-019: crossing/parallel/out-of-range/
                           shared-endpoint segment cases, straight and bent IntersectRoute),
                           CableLineHitTesterTests (M11/ADR-019: near/far/nearest-of-two),
                           TempDirectory helper (net8.0-windows, matching Ecad.Rendering's TFM) (86 tests)
```

Note: `Ecad.Rendering` targets `net8.0-windows` with `UseWPF=true` (not plain `net8.0`) because `SkiaSharp.Views.WPF` needs the WPF/Windows target framework to compile.

Dependency direction: `Ecad.App` depends on `Ecad.Core`, `Ecad.Data`, `Ecad.Rendering`, `Ecad.Reports`. `Ecad.Data`, `Ecad.Rendering`, `Ecad.Reports` depend on `Ecad.Core` only (not on each other, not on `Ecad.App`) — keeps domain logic UI- and storage-agnostic per ADR-001 / requirements principle that the data model is the source of truth.

Two SQLite databases per ADR-003: a per-project file (Project DB) and a shared `library.db` (Library DB). The `Part`/`PartPinTemplate`/`PartTerminalSpec`/`PartAccessory` tables exist with identical DDL in both — the Project DB's copy is a local cache populated when a Device first references a library Part, so a project file stays portable on its own.

Whole-solution build verified clean (`dotnet build EcadApp.sln`, 0 errors); 196 tests passing across three test projects (Core 8, Data 102, Rendering 86). Confirmed the app process actually starts (`ECAD` main window title observed via `Get-Process`); dialog/list visual behavior for M2 was click-tested live by the user. The M3 import engine was run end-to-end against a disposable copy of the real H2L Robotics `parts.edz`: 636 distinct parts imported into `%LOCALAPPDATA%\Ecad\library.db` in ~5.7s, 0 warnings; a later re-run backfilled images for 525 of those 636 parts. Verified `SymbolLibrary/*` files actually land in the build output directory (`bin/Debug/net8.0-windows/SymbolLibrary/`). The M5 schematic canvas (place/select/drag/rotate/rename/delete/undo/redo) was click-tested live by the user end-to-end, including two real bugs found and fixed along the way — see ADR-007. The M6 multi-placement devices, segment-aware tagging, and cross-reference display (including across two simultaneously-open page windows) were click-tested live by the user end-to-end, including three real bugs found and fixed along the way — see ADR-008. The M7 auto-connect wiring (pin geometry, manual wire drawing, direction-aware auto-connect/disconnect, junctions, wire numbering) went through several rounds of live user feedback before landing on its final direction-aware, geometrically-live behavior — see ADR-009. The interruption-points follow-up (two-ended symbol, Ctrl+Click page navigation, single-window-per-page dedup) was click-tested live by the user end-to-end right after. M8's Grid Editor (Devices/Connections/Cables tabs, bulk edit) went through five rounds of live-testing bug fixes before being fully confirmed working — see ADR-010 through ADR-015 (graphics-first creation rollback, the Part-caching FK crash, the Delete Selected no-op, the DataGrid placeholder-row crash, and the Device/Cable/Connection delete-semantics distinction). M9 added a 4th Terminations tab reusing every established M8 pattern as-is — builds and tests passed clean on the first real attempt, and the user confirmed the Terminations tab works live with zero bugs found. **M10 (application shell) is fully complete, all four phases** — see ADR-016/ADR-017: Phase 1 (schematic pages as tabs) found and fixed a real `DataContextChanged`/`SKElement` first-paint bug via live testing; grid-as-dots, auto-reopen-last-project, and palette drag-and-drop followed; Phases 2 and 3 (Grid Editor, then Parts Library/Symbol Browser, each as a singleton tab) were built and confirmed working; the pointer model went through three further rounds of live feedback (rubber-band multi-select, then swapped to left-click-drag with right-click becoming a Rotate/Delete/Undo/Redo context menu, then made direction-aware window-vs-crossing select, then fixed to keep tracking past the canvas's edge via mouse capture) with group move/delete as one `CompositeCommand` undo step throughout; Phase 4 (a docked, non-modal device-properties panel replacing the modal rename dialog) completed the milestone.
**M11 (wire & cable definition points) is now also fully complete** — see ADR-018/ADR-019: connection definition points replaced M7's automatic wire numbering with an explicit placement gesture, then went through a genuine root-cause redesign after live testing found moving a connected symbol made a definition point disappear (auto-connect's delete-and-recreate of the underlying `Connection` row was the real cause) — `DefinitionPoint` became a fully independent entity, the same shape `Placement` already used, surviving that churn via a nullable `ConnectionId` with `ON DELETE SET NULL`. Cable definition lines followed the same day, drawing on that just-learned lesson directly (`CableLine`/`CableLineCrossing` built the same way from the start) to resolve ADR-011's long-deferred "revisit once cable-definition-line drawing is built" note — auto-creating cable cores per crossed wire with zero upfront setup, per explicit user request. Both features went through several rounds of live-testing refinement (grid-snap bugs, rotation, red styling, independent crossing selectability) documented in the two ADRs and the 2026-07-11/12 `PROGRESS.md` log entries; every interaction was click-tested live, consistent with this project's established "no UI/canvas-interaction automated tests" pattern.

This section will be updated with real detail as each milestone lands — next up: M12 (reports engine) or another direction, to be decided with the user.
