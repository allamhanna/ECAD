# Project Structure

This file describes the actual folder/solution layout as it exists, kept in sync as the project is built. See `DECISIONS.md` ADR-001 for the stack rationale and `PROGRESS.md` for what's built vs planned.

## Current layout (as of M4 — Symbol format & starter set — complete)

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
      ViewModels/           MainViewModel (CommunityToolkit.Mvvm ObservableObject + RelayCommands:
                             NewProject, OpenProject, Save, SaveAs, CloseProject, AddPage,
                             ImportEplanPartsAsync, OpenPartsLibrary, Exit)
                             PartsLibraryViewModel — owns its own Library DB connection, search/filter
                             over all Parts + detail lookups (PinTemplates/TerminalSpecs/Accessories/
                             PreviewImage, the last built as a frozen BitmapImage from the cached BLOB)
      Views/                NewProjectDialog, AddPageDialog — plain code-behind modal dialogs
                             PartsLibraryWindow — non-modal, master-detail parts browser (read-only),
                             detail panel includes an Image control bound to PreviewImage
                             SymbolBrowserViewModel — loads SymbolLibrary/ via SymbolLibraryLoader,
                             rasterizes each via SymbolRasterizer, exposes thumbnails
                             SymbolBrowserWindow — non-modal, wrapped thumbnail grid (read-only)
      SymbolLibrary/         8 starter IEC-style symbols: RelayCoil, ContactNO, ContactNC, Terminal,
                             Motor3Phase, PushbuttonNO, Fuse, Lamp — each a plain .svg + .symbol.json
                             sidecar (see ADR-006), Content/CopyToOutputDirectory
      MainWindow.xaml(.cs)  File menu, page ListView (Function/Location/DocType/PageNumber/Type columns),
                             status bar; DataContext = MainViewModel
    Ecad.Core/             domain models & business logic (net8.0, no UI/storage deps)
      Models/              Project, Page, Device, DevicePin, Placement, PlacementPin, Connection, ConnectionEnd,
                            Cable, CableCore, Part, PartPinTemplate, PartTerminalSpec, PartAccessory, PartImage,
                            Organization, Classification, Symbol, ImportBatch, UdpDefinition, UdpValue
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
                             Dispose — the testable core behind Ecad.App's MainViewModel
      Repositories/         ProjectRepository (+ GetFirstProject, GetPages), DeviceRepository,
                             PlacementRepository (+ cross-reference query), ConnectionRepository,
                             CableRepository, PartRepository (+ upsert-by-ExternalKey, Replace*
                             child-row helpers, GetOrCreateOrganization, GetAllParts,
                             GetAllOrganizations, GetImage, UpsertImage), UdpRepository
                             — Dapper on top of Microsoft.Data.Sqlite
      Import/EplanEdzImporter.cs   parses a real EPLAN .edz (7z, read via SharpCompress) into the
                             Library DB — see ADR-004 for format quirks this handles, ADR-005 for
                             the unconditional (Added/Updated/Unchanged-independent) image backfill
      Import/EplanImportResult.cs  counts + warnings returned to the caller
    Ecad.Rendering/        canvas + SVG symbol rendering (net8.0-windows, UseWPF)
      Symbols/SymbolDefinition.cs      ConnectionPoint/TextPlaceholder/Variant POCOs + JSON (de)serialization
      Symbols/SymbolLibraryLoader.cs   scans a folder for *.symbol.json + matching .svg (see ADR-006)
      Symbols/SymbolRasterizer.cs      SkiaSharp + Svg.Skia: SVG bytes -> PNG byte array (no WPF dependency)
    Ecad.Reports/          report layout + QuestPDF generation (net8.0) — QuestPDF added, empty stub
  tests/
    Ecad.Core.Tests/       DeviceTagTests, PageTagTests (8 tests)
    Ecad.Data.Tests/       MigrationTests, ProjectSchemaTests, PartUpsertTests, ProjectSessionTests,
                           EplanEdzImporterTests (synthetic zip fixtures, see ADR-004/ADR-005),
                           TempSqliteFile helper (29 tests)
    Ecad.Rendering.Tests/  SymbolLibraryLoaderTests, SymbolRasterizerTests, TempDirectory helper
                           (net8.0-windows, matching Ecad.Rendering's TFM) (6 tests)
```

Note: `Ecad.Rendering` targets `net8.0-windows` with `UseWPF=true` (not plain `net8.0`) because `SkiaSharp.Views.WPF` needs the WPF/Windows target framework to compile.

Dependency direction: `Ecad.App` depends on `Ecad.Core`, `Ecad.Data`, `Ecad.Rendering`, `Ecad.Reports`. `Ecad.Data`, `Ecad.Rendering`, `Ecad.Reports` depend on `Ecad.Core` only (not on each other, not on `Ecad.App`) — keeps domain logic UI- and storage-agnostic per ADR-001 / requirements principle that the data model is the source of truth.

Two SQLite databases per ADR-003: a per-project file (Project DB) and a shared `library.db` (Library DB). The `Part`/`PartPinTemplate`/`PartTerminalSpec`/`PartAccessory` tables exist with identical DDL in both — the Project DB's copy is a local cache populated when a Device first references a library Part, so a project file stays portable on its own.

Whole-solution build verified clean (`dotnet build EcadApp.sln`, 0 errors); 43 tests passing across four test projects. Confirmed the app process actually starts (`ECAD` main window title observed via `Get-Process`); dialog/list visual behavior for M2 was click-tested live by the user. The M3 import engine was run end-to-end against a disposable copy of the real H2L Robotics `parts.edz`: 636 distinct parts imported into `%LOCALAPPDATA%\Ecad\library.db` in ~5.7s, 0 warnings; a later re-run backfilled images for 525 of those 636 parts. Verified `SymbolLibrary/*` files actually land in the build output directory (`bin/Debug/net8.0-windows/SymbolLibrary/`). The Parts Library and Symbol Browser windows' visual/interactive behavior is pending a user click-through.

This section will be updated with real detail as each milestone lands — next up: M5 (schematic canvas), to be decided with the user.
