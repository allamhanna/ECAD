# Progress

Status legend: `[ ]` not started · `[~]` in progress · `[x]` done

Stack: C#/.NET 8, WPF, SkiaSharp, Svg.Skia, QuestPDF, Microsoft.Data.Sqlite (see `DECISIONS.md` ADR-001).

## Phase 1 — Daily-use internal tool

- [x] **M0** — Toolchain + repo/solution scaffold
  - [x] Install .NET 8 SDK
  - [x] REQUIREMENTS.md / DECISIONS.md / PROGRESS.md / STRUCTURE.md in place
  - [x] git init + .gitignore + initial commit (`main` branch, commit `a33f132`)
  - [x] Solution + projects scaffolded (App/Core/Data/Rendering/Reports + tests)
  - [x] NuGet packages added, project references wired
  - [x] Solution builds cleanly end to end (`dotnet build EcadApp.sln` — 0 errors)
- [x] **M1** — Data model & SQLite layer: schema for Project/Page/Device/Placement/Connection/Cable/Part/Symbol + UDPs, versioned migrations, repository layer
  - [x] `Ecad.Core` domain models, enums, `DeviceTag`/`PageTag` IEC 81346 value objects
  - [x] Project DB + Library DB migrations (0001_initial.sql each), `MigrationRunner`
  - [x] Repositories: Project/Page, Device/DevicePin, Placement/PlacementPin (+ cross-reference query), Connection/ConnectionEnd, Cable/CableCore, Part family (+ upsert-by-ExternalKey), Udp
  - [x] `Ecad.Core.Tests` (8) + `Ecad.Data.Tests` (12) — migrations, cross-reference scenario, termination round-trip, cable/core assignment, UDP attach, Part upsert add/update/no-op — all passing
  - [x] Report/form-layout tables and symbol connection-point geometry deliberately deferred to M10/M4
- [x] **M2** — App shell & project management: create/open/save project, main window, page list panel
  - [x] `Ecad.Data/ProjectSession.cs` — Create/Open/AddPage/Checkpoint/Dispose, unit tested (4 new tests, 16 total in Ecad.Data.Tests)
  - [x] `Ecad.App`: `MainViewModel` (CommunityToolkit.Mvvm) + `NewProjectDialog`/`AddPageDialog` + rewritten `MainWindow` (menu, page ListView, status bar)
  - [x] Verified the app actually launches (`Ecad.App` process with `MainWindowTitle: ECAD` confirmed via `Get-Process`) — visual/interactive behavior (dialogs, ListView rendering) not verified by me; ask the user to click through New/Open/Add Page
  - [ ] "Save As" intentionally left out of scope for M2 (see DECISIONS.md note in the plan) — add later if needed
- [ ] **M3** — Parts library: CRUD, classification tree, custom properties (UDPs), EPLAN CSV/XML import wizard
- [ ] **M4** — Symbol format & starter IEC symbol set, symbol browser panel
- [ ] **M5** — Schematic canvas core: pan/zoom/grid snap, place/select/move/rotate symbols, undo/redo framework
- [ ] **M6** — Device tagging & cross-references (multi-placement devices, IEC 81346 tags)
- [ ] **M7** — Auto-connect wiring: orthogonal routing, junctions, auto wire numbering
- [ ] **M8** — Grid-based editing: Devices/Connections/Cables grids, bulk edit
- [ ] **M9** — Cable & termination model: cores, per-end termination toggle/parts, end-type classification
- [ ] **M10** — Reports engine: form/report JSON layout schema + the 4 Phase-1 reports (connection list, BOM, cable overview, cable manufacturing sheet)
- [ ] **M11** — Export: PDF (QuestPDF) and Excel/CSV
- [ ] **M12** — Non-functional hardening: autosave/crash recovery, perf pass (500+ elements/page), keyboard shortcuts

## Phase 2 — Robustness & authoring tools (backlog, not yet milestoned)
- In-app visual form/report layout editor
- In-app visual symbol editor
- Rule-based numbering schemes
- Project templates; lightweight revision/change tracking
- Terminal diagrams, wiring lists
- DXF export (optional)

## Phase 3 — Productization (backlog, not yet milestoned)
- Installer/updater, offline-capable licensing/activation
- Crash-safe autosave & project file integrity (backup/restore)
- Documentation, onboarding samples

## Log

- **2026-07-05** — Project folder created (`Documents\electrical CAD`). Requirements reviewed, stack decided (ADR-001), milestone roadmap defined, M0 started: .NET 8 SDK installed via winget.
- **2026-07-05** — M0 completed: solution `EcadApp.sln` scaffolded with `Ecad.App` (WPF), `Ecad.Core`, `Ecad.Data`, `Ecad.Rendering`, `Ecad.Reports`, plus `Ecad.Core.Tests`/`Ecad.Data.Tests`. NuGet packages added (SkiaSharp.Views.WPF + Svg.Skia on Rendering, QuestPDF on Reports, Microsoft.Data.Sqlite on Data). Project references wired per dependency direction in STRUCTURE.md. Full solution builds with 0 errors (some expected NU1701 compatibility warnings from SkiaSharp.Views.WPF/OpenTK targeting older .NET Framework TFMs — functional, revisit if it causes runtime issues once the canvas work starts in M5). Git repo initialized, initial commit `a33f132` on `main`. Next: M1 (data model & SQLite layer).
- **2026-07-05** — User revealed a real EPLAN parts export (`H2L Robotics/parts.edz`, ~236MB) and asked to migrate it in rather than rebuild a parts DB by hand. Inspected a disposable copy (original untouched): it's a 7z archive of per-part native XML (article data, pin templates, precise terminal/torque specs), not a binary DB — far richer than plain CSV. Symbol macros (`.ema`) turned out to be an unpublished internal EPLAN object-model dump, not practically convertible — M4 still ships hand-built SVG symbols. Recorded as ADR-003; folded into M1 schema design (`ExternalKey`, `SourceLastModifiedUtc`, `SourceImportBatchId`, `PartPinTemplate`, `PartTerminalSpec`).
- **2026-07-05** — M1 completed: full schema (Project DB + Library DB) via versioned SQL migrations, Dapper-based repositories (ADR-002), domain models/value objects in `Ecad.Core`. 20 tests passing across `Ecad.Core.Tests`/`Ecad.Data.Tests`, covering the multi-placement cross-reference mechanism, connection termination, cable/core assignment, UDPs, and Part upsert-by-`ExternalKey` (add/update/no-op). Whole-solution build still clean. Next: M2 (app shell & project management) or M3 (EPLAN parts import) — to be decided with the user.
- **2026-07-05** — M2 completed: `ProjectSession` (Ecad.Data) wraps create/open/add-page/checkpoint/dispose, tested (16 tests in Ecad.Data.Tests now, up from 12 — caught and fixed a real connection-leak bug in `ProjectSession.Open`'s error path along the way). `Ecad.App` got a real `MainViewModel` (CommunityToolkit.Mvvm), `NewProjectDialog`/`AddPageDialog`, and a rewritten `MainWindow` with a File menu, page `ListView`, and status bar. Confirmed the app process actually launches (`Get-Process` showed `Ecad.App` with `MainWindowTitle: ECAD`); visual/interactive correctness of the dialogs and list still needs a human click-through since I can't see or drive a rendered window myself. 24 tests passing total, solution builds clean. Next: M3 (EPLAN parts import) most likely, or M5 (schematic canvas) — to be decided with the user.
