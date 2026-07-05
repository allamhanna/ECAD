# Architecture Decision Log

Lightweight ADR log. Each entry: ID, status, context, decision, consequences. Append new entries at the bottom; never rewrite history — if a decision is reversed, add a new ADR that supersedes it and mark the old one's status as `Superseded by ADR-00X`.

---

## ADR-001: Technology Stack

**Status:** Accepted (2026-07-05)

**Context:**
Requirements (Section 4) call for a Windows-only, fully offline desktop app with a high-performance 2D canvas (hundreds of symbols/lines per page), first-class precise-layout PDF generation, SVG symbol rendering, local SQLite storage, and maintainability by a single engineer with AI assistance. Two candidate directions were on the table:
- (a) Tauri + TypeScript/React + Konva/PixiJS + web-based PDF
- (b) C#/.NET + WPF/Avalonia + SkiaSharp + QuestPDF

Environment check on the dev machine found: .NET 8 Desktop Runtime present, .NET SDK absent; Node.js, npm, Rust/cargo, and Visual Studio all absent; Git and winget available.

**Decision:**
C#/.NET 8, WPF, SkiaSharp (2D canvas rendering), Svg.Skia (SVG symbol parsing/rendering onto the same Skia surface), QuestPDF (report/PDF generation), Microsoft.Data.Sqlite (local storage).

**Rationale:**
- **PDF precision** — QuestPDF is purpose-built for exact, programmatic document layout (repeating rows, headers/footers, mm-precise positioning), which is exactly what cable manufacturing sheets and BOMs need. JS-side PDF options are weaker here or depend on a bundled Chromium, which Tauri does not ship.
- **Canvas performance** — SkiaSharp is a mature, fast 2D rendering surface, proven at the element counts this project targets (500+ per page).
- **SVG symbols** — Svg.Skia renders SVG directly onto the same Skia surface used for the canvas; no separate web rendering layer needed.
- **Platform fit** — the app is Windows-only and offline-only by requirement (Section 7), forever. WPF's native-Windows-only nature is not a downside here, and it sidesteps WebView2 runtime/version concerns entirely.
- **Toolchain footprint** — the dev machine had neither Node/npm nor Rust/cargo installed. Tauri needs both, plus the MSVC C++ build tools workload for Rust (multi-GB). The .NET 8 SDK is a single ~200MB installer, and the Desktop Runtime was already present.
- **Single-engineer maintainability** — WPF + C# is extremely well-trodden for line-of-business desktop apps (data grids, dialogs, docking panels), which matches the devices/connections/cables grid UI and parts/report editors called for in Sections 5–6.

**Consequences:**
- Windows-only forever is now baked into the UI layer choice (acceptable — matches requirements, no cross-platform ask exists).
- Team standardizes on C#/.NET idioms (MVVM, dependency injection, xUnit) for the whole codebase.
- SVG symbol format and rendering must go through Svg.Skia's supported SVG subset — full arbitrary SVG features are not guaranteed; symbol authoring conventions (ADR to follow) must stay within what Svg.Skia renders reliably.
- Report/form layouts are QuestPDF-native; the "declarative JSON layout" from Section 5.9 will be a schema *we* define that a rendering layer maps onto QuestPDF's fluent API, not a pre-existing template format.

---

## ADR-002: Data Access — Dapper + Plain SQL File Migrations

**Status:** Accepted (2026-07-05)

**Context:**
M1 needs a way to (a) version and apply schema changes to the two SQLite databases (Project DB, Library DB) and (b) map query results onto the domain models in `Ecad.Core` without excessive hand-written `IDataReader` boilerplate.

**Decision:**
- Migrations: plain numbered `.sql` files embedded as resources under `Ecad.Data/Migrations/{Project|Library}/000N_name.sql`, applied in order by `MigrationRunner`, tracked in a `schema_migrations(version, applied_at_utc)` table per database. No EF Core migrations.
- Data access: Dapper on top of `Microsoft.Data.Sqlite`, one repository class per aggregate in `Ecad.Data/Repositories/`.

**Rationale:**
- Plain SQL migration files are directly readable/diffable and match the "database migrations versioned from v1" NFR (Section 7) without adopting a heavy ORM migration pipeline.
- Dapper removes the reader-to-object boilerplate that raw ADO.NET requires for every query, while keeping every SQL statement explicit and visible in the repository — no LINQ-to-SQL translation layer to debug.

**Consequences:**
- Domain models in `Ecad.Core` stay plain POCOs (no persistence attributes); Dapper maps by column-name convention for classes with a parameterless constructor and settable properties.
- Where a repository needs a `record`-based intermediate row type (to do a value/enum conversion during mapping), its properties must match SQLite's underlying reader types exactly — `long` for INTEGER-affinity columns, `double` for REAL, not `int`/`bool`/`decimal`/enum types. Dapper's constructor-based materialization for `record`s requires an exact type match (unlike its lenient property-setter mapping), and SQLite's `Microsoft.Data.Sqlite` reader always reports INTEGER columns as `Int64` and REAL columns as `double` regardless of the declared column type. Conversion to the "nice" domain type (bool, enum, decimal) happens in that row type's `ToModel()` method. See `PartRepository.PartRow`, `ConnectionRepository.ConnectionEndRow`, `UdpRepository.UdpValueRow`, `ProjectRepository.PageRow` for the pattern.

---

## ADR-003: EPLAN `parts.edz` as Primary Parts-Import Source; Local Part Caching

**Status:** Accepted (2026-07-05)

**Context:**
The user has an existing EPLAN parts library export (`H2L Robotics/parts.edz`, ~236MB) and wants to migrate it in once, with the ability to re-import periodically, rather than rebuilding a parts database by hand or relying only on the plain CSV/XML import the original requirements doc (Section 5.7) assumed. I inspected a disposable copy of the file (the original on disk was never touched) and found:
- It's a 7-zip archive of ~2,225 files, not a binary database.
- `manifest.xml` indexes per-part XML file sets.
- `items\partxml\*.part.xml` — EPLAN's native "Parts Management" export per article: article number, descriptions, manufacturer/supplier, dimensions, weight, pricing, and `functiontemplate` elements (pin/connection designation, function category/group/id, symbol reference) that map directly onto `PartPinTemplate`.
- `items\partxml\*.connectionpoints.xml` — precise per-terminal position plus min/max cross-section, torque, and wire-count limits — maps directly onto `PartTerminalSpec` and is exactly what termination/ferrule matching (Section 6.3) needs.
- `items\partxml\*.accessorylist.xml` / `*.construction.xml` — sub-assembly composition and drilling/mounting data.
- `items\document\*` (datasheet PDFs) and `items\picture\*` (product photos) — usable as-is.
- `items\macro\*.ema` / `*_3D.ema` — schematic/3D symbol graphics. Technically well-formed XML (`EplanPxfRoot`), but the content is a serialized dump of EPLAN's internal object model (opaque numeric object/attribute codes, an internal object-reference graph) with no published schema. Evaluated and **rejected** as a symbol source — not practically convertible without a large, fragile reverse-engineering effort.

**Decision:**
- The EPLAN native XML export (`.edz`, unzipped as 7z) becomes the **primary** parts-import source (built in M3), ahead of plain CSV/XML — it carries far more structured data than a flat export and doesn't need a field-mapping wizard for the fields it does have.
- The Library DB's `Part` schema (M1) includes `ExternalKey` (the EPLAN article key), `SourceLastModifiedUtc` (from EPLAN's `P_PART_LASTCHANGE_DATE_UTC`), and `SourceImportBatchId`, specifically so a re-import can upsert by `ExternalKey` and use the source timestamp to decide add vs. update vs. skip — see `PartRepository.UpsertByExternalKey`.
- Symbol graphics are **not** sourced from `.ema` macros. M4 ships a small hand-built SVG symbol set instead, as originally planned.
- Project DB keeps a local cached copy of any `Part` (identical table schema to the Library DB) that a Device references, so a project file stays portable/self-contained without needing the shared Library DB present — this satisfies the "referenced, with per-project caching/copy" language in Section 5 of the requirements doc.

**Consequences:**
- M3 (parts import) will parse XML directly (`System.Xml`), not CSV — the CSV/XML wizard from Section 6.6 becomes a secondary/fallback import path rather than the primary one.
- Panel-layout-oriented data (`construction.xml` drilling patterns) is out of scope for Phase 1 per the requirements doc (Section 8) and won't be imported even though it's available in the source file.
- If EPLAN's export format changes in a future version, the XML parser in M3 needs revisiting; the schema itself (Library DB `Part`/`PartPinTemplate`/`PartTerminalSpec`) is source-agnostic and shouldn't need to change.

---

## ADR-004: SharpCompress for Reading `.edz` (7z) Archives; Real-Data Import Robustness

**Status:** Accepted (2026-07-05)

**Context:**
M3 needed a way to read the 7-Zip-format `.edz` archive from .NET without external process dependencies (`7z.exe`) that would break the "fully offline, installable" requirement. .NET has no built-in 7z support (`System.IO.Compression` is zip-only).

**Decision:**
Added **SharpCompress** (pure managed .NET library) and read via its generic `ArchiveFactory.OpenArchive`/`IArchive`/`IArchiveEntry` interfaces rather than the 7z-specific `SevenZipArchive` type. SharpCompress can only *read* 7z (no write support), and a real `.edz` is far too large and proprietary to commit as a test fixture — using the generic interface means `EplanEdzImporter` doesn't care what container format it's reading, so `Ecad.Data.Tests` builds tiny synthetic **`.zip`** fixtures (via `System.IO.Compression`, no SharpCompress writer needed) with the identical internal folder layout, exercising the exact same parsing code the real 7z path uses.

**Consequences — real-data robustness fixes found only by running against the actual H2L Robotics export (not caught by synthetic fixtures):**
Running the importer against a disposable copy of the real file surfaced three issues no amount of guessing at the schema would have caught:
1. SharpCompress reports entry keys with **forward slashes**, regardless of `7z l`'s own backslash-style display — `EplanEdzImporter`'s path-building was fixed to match.
2. The real export has **duplicate keys** at both the archive level (e.g. two identical `teejet.manufacturer.xml` entries) and within a single part's manifest `<items>` block (e.g. duplicate `groupsymbolmacro` entries) — dictionary construction now uses group-by-first-wins instead of a plain `ToDictionary`, which threw on the first duplicate.
3. Some real `<terminalPosition>` elements have no `name` attribute, which used to silently become a C# `null` (LINQ to XML's explicit string conversion doesn't throw on a missing attribute) and only surfaced as a SQLite `NOT NULL` constraint violation at insert time — now falls back to `#{pos}`.
4. Per-part processing is now wrapped in a try/catch that records a warning and continues, rather than letting one malformed package abort the entire import and silently drop every part after it in the manifest.

A full run against the real 236MB/636-part export completed in ~5.7 seconds with 0 warnings after these fixes. All four issues have regression tests in `EplanEdzImporterTests`.

---

## ADR-005: Part Preview Images Stored as BLOBs, Not File-Path References

**Status:** Accepted (2026-07-05)

**Context:**
The user asked for a visual preview (product photo) in the Parts Library window. `Part.PictureFilePath` already existed as a column, but it was populated from EPLAN's own unresolvable path style (e.g. `$(MD_IMG)\Siemens\...`) and was never actually written to by the M3 importer (pictures were explicitly out of scope for that pass). Extracting images to loose files on disk and storing a real local path in that column was the obvious-looking option, but it directly conflicts with a principle already established in ADR-003: the Project DB caches a copy of any referenced `Part` (identical schema to the Library DB) specifically so a project file is a single, self-contained, copyable SQLite file (also an explicit NFR in Section 7 of the requirements). A file-path reference silently breaks the moment the `.ecad` file is copied or moved without a side-folder of images coming with it.

**Decision:**
Added a new `PartImage` table (`Id`, `PartId`, `ContentType`, `ImageData BLOB`) — one row per part with an available picture — in both the Project DB and Library DB migrations (`0002_part_images.sql`, same intentional identical-DDL pattern as the `Part` family). `Part.PictureFilePath` itself is left alone (still unused/reserved).

**Consequences:**
- `EplanEdzImporter`'s image extraction runs **unconditionally**, not gated on the Added/Updated/Unchanged upsert result. This mattered in practice: re-running the importer to backfill images into the 636 already-imported parts found they all come back `Unchanged` (their source timestamp hasn't moved), so gating on that result would have skipped every one of them. `PartRepository.UpsertImage` is idempotent (delete-then-insert), so running it on every re-import is cheap and safe.
- This surfaced a real, separate bug while implementing it: `PartRepository.UpsertByExternalKey` never set `part.Id` on its `Unchanged` return path, so the image backfill was silently writing against `PartId = 0` for every already-existing part. Fixed by setting `part.Id = existing.Id` before returning `Unchanged`, with a regression test (`Import_UnchangedPartMissingImage_BackfillsItOnReimport`).
- Backfilling the real library: re-ran the importer against a disposable copy of the real `parts.edz` after this change — **525 of 636 parts got an image, 0 warnings**, migration 0002 applied cleanly on top of the already-populated (schema v1) `library.db`.
- Images are lazy-loaded (`PartRepository.GetImage`) only when a part is selected in the UI, not as part of `GetAllParts()`, to avoid pulling ~600 BLOBs into memory just to render the list.

---

## ADR-006: Symbol Format — Plain SVG + JSON Sidecar, Bundled Not Database-Driven

**Status:** Accepted (2026-07-05)

**Context:**
M1 deliberately left `Ecad.Core.Models.Symbol` minimal, deferring the actual SVG symbol format to M4 (Section 5.8, Section 9 of the requirements). M3 already ruled out reusing EPLAN's own `.ema` macros as a symbol source (ADR-003 — proprietary, unpublished internal object-model dump). M4 designs the format from scratch and ships a small starter IEC-style set.

**Decision:**
- A symbol is two files: `{Name}.svg` (a plain, standard SVG — editable in any vector tool, no custom namespaces or embedded metadata) and `{Name}.symbol.json` (our metadata: `connectionPoints` [pin, x, y, direction], `textPlaceholders` [kind, x, y, anchor], `variants` [name, rotationDegrees, mirrored]). Keeping metadata in a sidecar rather than embedded in the SVG means the graphic survives round-tripping through an external SVG editor without special handling, matching the requirements' "open, editable underlying formats" principle.
- Every symbol shares a `0 0 40 40` viewBox — one nominal grid cell — so connection-point coordinates are consistent across the library regardless of final on-canvas scale (relevant once M5 places symbols on a real page).
- Storage is **bundled with the app** (`Ecad.App/SymbolLibrary/*.svg`/`*.symbol.json`, `Content`/`CopyToOutputDirectory`), resolved via `AppContext.BaseDirectory` — not database-driven yet. The Library DB's `Symbol` table (from M1) stays unused until M5/M6 actually need a `Placement` to reference a specific symbol row; populating it now would be premature since nothing consumes it.
- Rendering for the M4 Symbol Browser is **static rasterization**, not the interactive `SkiaSharp.Views.WPF` canvas control: `SymbolRasterizer` (plain `SkiaSharp` + `Svg.Skia`, no WPF dependency) renders each symbol's SVG to a PNG byte array once, shown via a WPF `Image`/`BitmapImage` — the same `byte[] → BitmapImage` pattern already used for part preview images (ADR-005). This keeps the parsing/rasterization logic fully unit-testable and defers the actual interactive canvas control to M5, where it's needed for real.
- Starter set: 8 hand-authored IEC 60617-style symbols (Relay/Contactor Coil, NO Contact, NC Contact, Terminal, 3-Phase Motor, Pushbutton NO, Fuse, Lamp/Indicator) — simple line/circle/rect geometry, recognizable but not intended as polished final artwork.

**Consequences:**
- `Ecad.Rendering` gained a new `Symbols/` namespace (`SymbolDefinition` + child POCOs, `SymbolLibraryLoader`, `SymbolRasterizer`) and its own test project, `Ecad.Rendering.Tests` (net8.0-windows, matching `Ecad.Rendering`'s TFM) — the first tests for that project since M0.
- The loader tolerates real-world-shaped problems the same way M3's importer does: a `.symbol.json` with no matching `.svg`, or one malformed JSON file, produces a warning and is skipped rather than aborting the whole library load.
- Symbol placement, rotation/mirror application, and connection-point-driven wiring are explicitly out of scope here — that's M5 (canvas) and M6 (device tagging/cross-references).

---
