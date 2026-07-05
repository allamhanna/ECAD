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
- [ ] **M1** — Data model & SQLite layer: schema for Project/Page/Device/Placement/Connection/Cable/Part/Symbol/Form-Report + UDPs, versioned migrations, repository layer
- [ ] **M2** — App shell & project management: create/open/save project, main window, page list panel
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
