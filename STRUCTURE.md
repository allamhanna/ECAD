# Project Structure

This file describes the actual folder/solution layout as it exists, kept in sync as the project is built. See `DECISIONS.md` ADR-001 for the stack rationale and `PROGRESS.md` for what's built vs planned.

## Current layout (as of M0 complete)

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
      App.xaml(.cs)        application entry point
      MainWindow.xaml(.cs) placeholder main window — no real UI yet
    Ecad.Core/             domain models & business logic (net8.0, no UI/storage deps) — empty stub
    Ecad.Data/             SQLite access layer (net8.0) — Microsoft.Data.Sqlite added, empty stub
    Ecad.Rendering/        canvas + SVG symbol rendering (net8.0-windows, UseWPF) — SkiaSharp.Views.WPF + Svg.Skia added, empty stub
    Ecad.Reports/          report layout + QuestPDF generation (net8.0) — QuestPDF added, empty stub
  tests/
    Ecad.Core.Tests/       xUnit, references Ecad.Core
    Ecad.Data.Tests/       xUnit, references Ecad.Data
```

Note: `Ecad.Rendering` targets `net8.0-windows` with `UseWPF=true` (not plain `net8.0`) because `SkiaSharp.Views.WPF` needs the WPF/Windows target framework to compile.

Dependency direction: `Ecad.App` depends on `Ecad.Core`, `Ecad.Data`, `Ecad.Rendering`, `Ecad.Reports`. `Ecad.Data`, `Ecad.Rendering`, `Ecad.Reports` depend on `Ecad.Core` only (not on each other, not on `Ecad.App`) — keeps domain logic UI- and storage-agnostic per ADR-001 / requirements principle that the data model is the source of truth.

Whole-solution build verified clean (`dotnet build EcadApp.sln`, 0 errors). Git repo initialized on `main`, initial scaffold committed.

This section will be updated with real detail (actual namespaces, key classes per project) as each milestone lands — next up is M1 (data model & SQLite schema/migrations in `Ecad.Core`/`Ecad.Data`).
