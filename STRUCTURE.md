# Project Structure

This file describes the actual folder/solution layout as it exists, kept in sync as the project is built. See `DECISIONS.md` ADR-001 for the stack rationale and `PROGRESS.md` for what's built vs planned.

## Current layout

```
electrical CAD/
  REQUIREMENTS.md      original requirements doc
  DECISIONS.md         architecture decision log (ADRs)
  PROGRESS.md           milestone checklist + running log
  STRUCTURE.md          this file
```

Solution/code scaffold not yet created (in progress as part of M0 — see PROGRESS.md).

## Planned layout (target end of M0)

```
electrical CAD/
  REQUIREMENTS.md, DECISIONS.md, PROGRESS.md, STRUCTURE.md
  .gitignore
  EcadApp.sln
  src/
    Ecad.App/          WPF startup project — views, viewmodels, app composition root
    Ecad.Core/         domain models & business logic, no UI or storage dependencies
    Ecad.Data/         SQLite access, versioned migrations, repositories
    Ecad.Rendering/    SkiaSharp canvas rendering + SVG symbol loading (Svg.Skia)
    Ecad.Reports/      form/report layout schema + QuestPDF-based generation
  tests/
    Ecad.Core.Tests/
    Ecad.Data.Tests/
```

Dependency direction: `Ecad.App` depends on `Ecad.Core`, `Ecad.Data`, `Ecad.Rendering`, `Ecad.Reports`. `Ecad.Data`, `Ecad.Rendering`, `Ecad.Reports` depend on `Ecad.Core` only (not on each other, not on `Ecad.App`) — keeps domain logic UI- and storage-agnostic per ADR-001 / requirements principle that the data model is the source of truth.

This section will be updated with real detail (actual namespaces, key classes per project) as each milestone lands.
