# Project Structure

This file describes the actual folder/solution layout as it exists, kept in sync as the project is built. See `DECISIONS.md` ADR-001 for the stack rationale and `PROGRESS.md` for what's built vs planned.

## Current layout (as of M7 — Auto-connect wiring — complete)

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
                             (M7: a wire's metadata only — ConnectionId/From/To/WireNumber, no cached
                             geometry, since its route is recomputed live from current pin positions every
                             render), Commands.cs (IUndoableCommand implementations: PlaceSymbolCommand,
                             AttachPlacementCommand (M6: place on an existing Device), MoveCommand,
                             RotateCommand, RenameTagCommand (M6: full Function/Location/Designation),
                             DeleteCommand — see ADR-007/ADR-008 for the undo-of-delete recreate design,
                             now branching on whether the Device survived; CreateConnectionCommand,
                             DeleteConnectionCommand, RenameWireNumberCommand, RenumberWiresCommand (M7,
                             see ADR-009 for the recreate-not-restore/wire-numbering design))
      ViewModels/           MainViewModel (CommunityToolkit.Mvvm ObservableObject + RelayCommands:
                             NewProject, OpenProject, Save, SaveAs, CloseProject, AddPage,
                             ImportEplanPartsAsync, OpenPartsLibrary, OpenSymbolBrowser, Exit;
                             + OpenPage(Page) launches SchematicPageWindow)
                             PartsLibraryViewModel — owns its own Library DB connection, search/filter
                             over all Parts + detail lookups (PinTemplates/TerminalSpecs/Accessories/
                             PreviewImage, the last built as a frozen BitmapImage from the cached BLOB)
                             SchematicPageViewModel — owns a page's CanvasViewport/Placements/Connections/
                             UndoRedoStack; translates raw mouse/keyboard input (reported by
                             SchematicPageWindow's code-behind) into IUndoableCommand executions;
                             caches loaded SKSvg per symbol name (IDisposable — see ADR-007);
                             subscribes to ProjectSession.PlacementsChanged/ConnectionsChanged (M6/M7,
                             ADR-008/ADR-009) so this page's tags/cross-references/wires stay live even
                             when another open window's page is what actually changed. M7 additions:
                             pin hit-testing ahead of placement hit-testing, a wire-draw drag state
                             parallel to the existing placement-drag/pan state, direction-aware magnetic
                             pin-snap while placing/dragging (ApplyPinMagnetSnap), and RunAutoConnect
                             (auto-connect *and* auto-disconnect a moved/rotated placement's wiring
                             against its current geometry every time — see ADR-009)
      Views/                NewProjectDialog, AddPageDialog — plain code-behind modal dialogs
                             PartsLibraryWindow — non-modal, master-detail parts browser (read-only),
                             detail panel includes an Image control bound to PreviewImage
                             SymbolBrowserViewModel — loads SymbolLibrary/ via SymbolLibraryLoader,
                             rasterizes each via SymbolRasterizer, exposes thumbnails
                             SymbolBrowserWindow — non-modal, wrapped thumbnail grid (read-only)
                             DeviceTagDialog — segment-aware IEC 81346 tag editor (M6): Function/
                             Location/Designation fields with a live tag preview and uniqueness
                             validation; placement mode adds a device picker ("New Device" or an
                             existing one to attach to); rename mode edits a Device's segments directly
                             WireNumberDialog — minimal single-field wire-number rename dialog (M7),
                             same shape as M5's original pre-M6 DeviceTagDialog
                             SchematicPageWindow — non-modal per-page canvas: SKElement (first
                             interactive use of SkiaSharp.Views.WPF, IgnorePixelScaling="True" per
                             ADR-007) + a palette sidebar of the M4 starter symbols + a "Renumber Wires"
                             toolbar button (M7); mouse/keyboard events are translated to
                             SchematicPageViewModel calls, nothing else lives in the code-behind
      SymbolLibrary/         8 starter IEC-style symbols: RelayCoil, ContactNO, ContactNC, Terminal,
                             Motor3Phase, PushbuttonNO, Fuse, Lamp — each a plain .svg + .symbol.json
                             sidecar (see ADR-006), Content/CopyToOutputDirectory
      MainWindow.xaml(.cs)  File menu, page ListView (Function/Location/DocType/PageNumber/Type columns,
                             double-click a row to open SchematicPageWindow), status bar;
                             DataContext = MainViewModel
    Ecad.Core/             domain models & business logic (net8.0, no UI/storage deps)
      Models/              Project, Page, Device, DevicePin, Placement, PlacementPin, Connection, ConnectionEnd,
                            Cable, CableCore, Part, PartPinTemplate, PartTerminalSpec, PartAccessory, PartImage,
                            Organization, Classification, Symbol, ImportBatch, UdpDefinition, UdpValue,
                            PlacementWithSymbol (+ FunctionSegment/LocationSegment/Siblings/Pins, M6/M7),
                            SiblingPlacementRef (PlacementId, PageId, PageLabel — M6 cross-reference display),
                            PlacementPinInfo (DevicePinId, Name — M7, lets the canvas resolve a pin's world
                            position via the symbol's matching-named connection point)
      ValueObjects/         DeviceTag (=Function+Location-Tag), PageTag (=Function+Location&DocType/Page)
      Enums/                PageType, PartType, TerminationType, UdpDataType, UdpEntityType,
                            ConnectionEndDesignator, ImportSourceType
    Ecad.Data/             SQLite access layer (net8.0)
      Migrations/Project/0001_initial.sql   Project DB schema (+ local cached Part tables)
      Migrations/Library/0001_initial.sql   Library DB schema (Part/PartPinTemplate/PartTerminalSpec/
                                             PartAccessory DDL intentionally identical to Project's copy)
      Migrations/{Project,Library}/0002_part_images.sql   PartImage table (BLOB, see ADR-005) —
                                             again identical DDL in both databases
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
                             AreConnected, RenameWireNumber, IsWireNumberAvailable, SuggestNextWireNumber,
                             RenumberAllWires/ApplyWireNumbers, GetPlacementPins, the ConnectionsChanged
                             event (ADR-009), and fixed DeletePlacement to delete dependent Connections
                             before the pins they reference (no ON DELETE CASCADE on that FK)
      PlacementDeletionResult.cs   M6: what DeletePlacement did (whole Device removed, or just the
                             placement) — tells DeleteCommand.Undo() which recreate strategy to use
      Repositories/         ProjectRepository (+ GetFirstProject, GetPages), DeviceRepository
                             (+ DeleteDevice, UpdateDeviceTag with Function/Location, GetAllDevices,
                             FindByTag, SuggestNextDesignation — M6), PlacementRepository
                             (+ GetOrCreateSymbol, UpdatePosition, UpdateRotation, GetPlacementsForPage,
                             GetSiblingPlacementRefs, GetPlacementPinNames, CountPlacementsForDevice,
                             DeleteExclusiveDevicePinsForPlacement, DeletePlacement — M6; GetPlacementPins
                             — M7), ConnectionRepository (M7: DeleteConnection, GetConnectionsForPage,
                             GetConnectionsForDevicePin, AreConnected, UpdateWireNumber, FindByWireNumber,
                             GetAllWireNumbers, GetConnectionIdsForRenumbering — was bare Insert/Get only
                             since M1), CableRepository, PartRepository (+ upsert-by-ExternalKey, Replace*
                             child-row helpers, GetOrCreateOrganization, GetAllParts, GetAllOrganizations,
                             GetImage, UpsertImage), UdpRepository — Dapper on top of Microsoft.Data.Sqlite
      Import/EplanEdzImporter.cs   parses a real EPLAN .edz (7z, read via SharpCompress) into the
                             Library DB — see ADR-004 for format quirks this handles, ADR-005 for
                             the unconditional (Added/Updated/Unchanged-independent) image backfill
      Import/EplanImportResult.cs  counts + warnings returned to the caller
    Ecad.Rendering/        canvas + SVG symbol rendering (net8.0-windows, UseWPF)
      Canvas/CanvasViewport.cs         pan/zoom/grid-spacing state + WorldToScreen/ScreenToWorld/SnapToGrid
      Canvas/PlacementHitTester.cs     rotation-aware topmost-hit test over a placement list
      Canvas/UndoRedoStack.cs          generic IUndoableCommand push/undo/redo, no WPF/Ecad.Data dependency
      Canvas/SchematicCanvasRenderer.cs  pure SkiaSharp draw: grid, wires (drawn before placements),
                             junction dots, each placement (rotated/mirrored/scaled to its world
                             footprint), selection highlight, device tag text, sibling cross-reference
                             text (M6, only drawn when siblings exist), pin markers (M7, drawn on top of
                             placements), wire-draw dashed preview (M7, drawn topmost)
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
      Canvas/WireHitTester.cs          M7: proximity-based pin and wire (point-to-polyline) hit-testing
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
                           DeleteConnection, wire-number rename+uniqueness, SuggestNextWireNumber,
                           RenumberAllWires ordering, DeletePlacement cleans up dependent Connections),
                           EplanEdzImporterTests (synthetic zip fixtures, see ADR-004/ADR-005),
                           TempSqliteFile helper (47 tests)
    Ecad.Rendering.Tests/  SymbolLibraryLoaderTests, SymbolRasterizerTests, CanvasViewportTests,
                           PlacementHitTesterTests, UndoRedoStackTests, PlacementPinGeometryTests,
                           OrthogonalRouterTests, AutoConnectDetectorTests, JunctionDetectorTests,
                           WireHitTesterTests, TempDirectory helper
                           (net8.0-windows, matching Ecad.Rendering's TFM) (58 tests)
```

Note: `Ecad.Rendering` targets `net8.0-windows` with `UseWPF=true` (not plain `net8.0`) because `SkiaSharp.Views.WPF` needs the WPF/Windows target framework to compile.

Dependency direction: `Ecad.App` depends on `Ecad.Core`, `Ecad.Data`, `Ecad.Rendering`, `Ecad.Reports`. `Ecad.Data`, `Ecad.Rendering`, `Ecad.Reports` depend on `Ecad.Core` only (not on each other, not on `Ecad.App`) — keeps domain logic UI- and storage-agnostic per ADR-001 / requirements principle that the data model is the source of truth.

Two SQLite databases per ADR-003: a per-project file (Project DB) and a shared `library.db` (Library DB). The `Part`/`PartPinTemplate`/`PartTerminalSpec`/`PartAccessory` tables exist with identical DDL in both — the Project DB's copy is a local cache populated when a Device first references a library Part, so a project file stays portable on its own.

Whole-solution build verified clean (`dotnet build EcadApp.sln`, 0 errors); 113 tests passing across three test projects (Core 8, Data 47, Rendering 58). Confirmed the app process actually starts (`ECAD` main window title observed via `Get-Process`); dialog/list visual behavior for M2 was click-tested live by the user. The M3 import engine was run end-to-end against a disposable copy of the real H2L Robotics `parts.edz`: 636 distinct parts imported into `%LOCALAPPDATA%\Ecad\library.db` in ~5.7s, 0 warnings; a later re-run backfilled images for 525 of those 636 parts. Verified `SymbolLibrary/*` files actually land in the build output directory (`bin/Debug/net8.0-windows/SymbolLibrary/`). The Parts Library and Symbol Browser windows' visual/interactive behavior is pending a user click-through. The M5 schematic canvas (place/select/drag/rotate/rename/delete/undo/redo) was click-tested live by the user end-to-end, including two real bugs found and fixed along the way — see ADR-007. The M6 multi-placement devices, segment-aware tagging, and cross-reference display (including across two simultaneously-open page windows) were click-tested live by the user end-to-end, including three real bugs found and fixed along the way — see ADR-008. The M7 auto-connect wiring (pin geometry, manual wire drawing, direction-aware auto-connect/disconnect, junctions, wire numbering) went through several rounds of live user feedback before landing on its final direction-aware, geometrically-live behavior — see ADR-009.

This section will be updated with real detail as each milestone lands — next up: M8 (grid-based editing) or the deferred cross-page "interruption point" wiring follow-up, to be decided with the user.
