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

## ADR-007: M5 Schematic Canvas — Undo-of-Delete Recreates Rather Than Restores; Two Real-Bug Fixes

**Status:** Accepted (2026-07-06)

**Context:**
M5 is the first milestone to use `SkiaSharp.Views.WPF`'s interactive `SKElement` (M4 only ever rasterized static thumbnails, per ADR-006) and the first to give the undo/redo framework real commands to drive: placing, moving, rotating, renaming, and deleting a `Placement`.

**Decision — undo-of-delete is a recreate, not a restore:**
The M1 schema cascades a `Device` delete through `DevicePin`/`Placement`/`PlacementPin` (`ON DELETE CASCADE`), so by the time `DeleteCommand.Do()` returns there is nothing left in the database to "undelete" by ID. `DeleteCommand.Undo()` instead calls `ProjectSession.PlaceSymbol` again with the deleted placement's captured symbol/tag/position/rotation, producing a new `Device`/`Placement` with new row IDs that is visually and functionally identical. This is simpler than adding soft-delete or ID-preserving restore logic to the schema, and the row IDs themselves aren't user-visible anywhere yet.

**Two real bugs found via live user testing (not caught by the 64 automated tests, since both are runtime/rendering concerns automated tests don't exercise):**
1. **`AccessViolationException` crash on the first repaint after placing a symbol.** `SchematicPageViewModel` originally loaded each symbol's SVG via `using var svg = new SKSvg(); ... return svg.Load(stream);` — disposing the `SKSvg` at the end of the method. `SKSvg.Dispose()` also frees its `Picture`'s native memory, so the returned `SKPicture` (cached for reuse across repaints) pointed at freed memory the moment the method returned; the next call to `SKPicture.CullRect` during rendering read that freed memory and crashed the whole process. Fixed by caching the `SKSvg` instances themselves (kept alive for the `SchematicPageViewModel`'s lifetime, disposed together with it) rather than just their `Picture`.
2. **Placed symbols couldn't be reliably selected or dragged afterward.** `SKElement.PaintSurface` reports its canvas size in physical pixels (`SKImageInfo.Width/Height` scaled by the display's DPI), while WPF mouse events (`MouseEventArgs.GetPosition`) report DIP/logical coordinates. On any display with scaling ≠ 100%, a placement rendered at a pixel position that didn't correspond 1:1 to the DIP coordinates used for hit-testing, so a placement could render visibly off from where it was actually stored — clicking on what looked like the symbol missed its hit-test box. Fixed by setting `SKElement.IgnorePixelScaling = "True"` (confirmed the property exists and its effect via a throwaway reflection probe against the real `SkiaSharp.Views.WPF.SKElement` type before applying it, per the "verify third-party APIs before use" habit from M3/M4).

**Consequences:**
- Any future code that loads an SVG via `Svg.Skia`'s `SKSvg` and needs the resulting `SKPicture` to outlive the method that loaded it must keep the `SKSvg` instance alive too — this is now a known gotcha, not just fixed in one place.
- `SchematicPageViewModel` implements `IDisposable` (disposing its cached `SKSvg`s); `SchematicPageWindow.OnClosed` disposes it. This is the first `Ecad.App` ViewModel with this shape besides `MainViewModel`.
- `SKElement.IgnorePixelScaling="True"` is now the standard setting for any future interactive Skia canvas in this app — without it, correctness of mouse-driven interaction depends on the display's DPI scaling being exactly 100%.

---

## ADR-008: M6 Device Tagging & Cross-References — Simple Auto-Numbering, Placement-Level Deletion, Cross-Window Live Sync

**Status:** Accepted (2026-07-06)

**Context:**
M6 is the first milestone to actually exercise the multi-placement-device design M1 built ahead of need (no unique constraint on `Placement.DeviceId`, `PlacementPin` as "the cross-reference mechanism," `PlacementRepository.GetSiblingPlacementIds` proven only by a from-scratch repository test). It replaces M5's 1-device-1-placement application-layer assumption with real support for attaching a new Placement to an existing Device, segment-aware IEC 81346 tag editing, and live cross-reference display between sibling placements.

**Decision — auto-suggested Designation is a plain sequential number, not a numbering engine:**
Requirements 6.1 asks for "auto-assign tag using page's Function+Location context" but explicitly defers "rule-based numbering schemes (configurable)" to Phase 2, and never specifies an actual algorithm (letter-code-per-symbol-kind, padding, reuse-after-delete, etc.). `DeviceRepository.SuggestNextDesignation` does the simplest thing that satisfies the stated requirement: scan existing Devices sharing the same Function+Location, extract the highest trailing integer from their `DeviceTagSegment`, suggest `+1` (or `1`). It's a starting point the user freely overwrites in the Designation field (e.g. typing `K1` instead of accepting `1`) — no symbol-kind-to-IEC-letter inference exists anywhere in the symbol metadata, and inventing one now would be exactly the kind of scope creep the M5 milestone-scope-discipline lesson warns against.

**Decision — deletion and its undo became placement-level, not device-level:**
M5's `DeletePlacement` always deleted the whole Device, correct only because M5 had no multi-placement devices. M6's version: delete the DevicePins referenced *only* by the placement being removed (leaving a sibling placement's pins alone), delete the Placement, then delete the Device too only if it has no placements left. The result (`PlacementDeletionResult`) tells `DeleteCommand.Undo()` which branch to take — recreate a whole new Device (ADR-007's original recreate-not-restore strategy, unchanged for the single-placement case) or just a new Placement on the Device that's still there.

**Decision — cross-reference "live" means genuinely live, across every open window, via a shared-session event:**
The first design draft scoped "live" to just the page window where a change happened, planning to document cross-window staleness as an accepted limitation. Live user testing immediately surfaced this as actually wrong, not an acceptable trade-off — deleting a contact placement on one open page window left a stale cross-reference on another already-open window for the coil's page. Since every `SchematicPageWindow` for a project already shares one `ProjectSession` instance, the fix is a plain `ProjectSession.PlacementsChanged` event raised after any placement add/attach/delete or device rename; every `SchematicPageViewModel` subscribes in its constructor and unsubscribes in `Dispose()`, re-syncing its own page's tags and sibling labels from the DB whenever it fires. No message bus or WPF dependency needed — it's a plain C# event on an object every consumer already holds a reference to.

**Consequence — a new Dapper materialization gotcha found (extends ADR-002):**
`PlacementRepository.GetSiblingPlacementRefs`'s first version selected a `COALESCE(pg.PageNumberSegment, '#' || p2.PageId)` computed column straight into the `SiblingPlacementRef` record. This failed for the common zero-sibling case with "a constructor matching `(long, long, byte[])` is required" — Dapper generates its deserializer from the reader's column schema before checking whether any rows exist, and Microsoft.Data.Sqlite can't resolve a concrete CLR type for a computed expression column without an actual row to inspect, apparently defaulting to blob. Fixed the same way ADR-002 recommends for constructor-strict record types: query into a plain settable-property row class instead (lenient mapping tolerates the ambiguous schema), and build the label string in C# after the fact rather than in SQL.

**Consequences:**
- `Device.PartId` remains unused — Part assignment to a Device was explicitly confirmed out of scope for M6 (likely M8 or its own slice).
- The "column" component of "page/column references" (Section 5.4) isn't implemented — no title-block/frame grid model exists yet to give "column" meaning; cross-references show page number only.
- Any future cross-window "live" feature (e.g. M7's connection routing, or the EPLAN-style "interruption point" jump-navigation the user asked about right after M6) should reach for the same `ProjectSession`-event pattern rather than re-deriving one.

---

## ADR-009: M7 Auto-Connect Wiring — Direction-Aware Facing Rule, Live Recomputed Routing, Auto-Disconnect

**Status:** Accepted (2026-07-07)

**Context:**
M7 is the first milestone to use `SymbolConnectionPoint.X/Y/Direction` — parsed from every symbol's `.symbol.json` sidecar since M4, but never consumed for anything beyond deriving `DevicePin` names. Section 5.5/6.1 describe the target behavior only at the outcome level ("touching a symbol pin creates a Connection"; "connection lines re-route when symbols move"; "simple orthogonal routing... note upgrade path") without specifying the interaction model, so the exact rule was worked out through several rounds of live user feedback rather than a single up-front design.

**Decision — no stored route; a wire's path is recomputed fresh from its pins' current positions on every render:**
`Connection` (since M1) is purely `FromDevicePinId`/`ToDevicePinId` — no waypoint/polyline table was added. `OrthogonalRouter.Route(from, to)` computes a straight line or one-bend L-shape from both pins' *current* world positions every time a page renders. This is what makes "connection lines re-route when symbols move" free — there is no cached geometry to keep in sync when a placement moves, only the two pins' positions, which are themselves always recomputed live from placement position/rotation/mirror (`PlacementPinGeometry`, the same transform `SchematicCanvasRenderer` already applies to the symbol picture, applied here to a connection point instead).

**Decision — the auto-connect rule is direction-aware "facing on the same grid line," not proximity, arrived at after the first version failed in practice:**
The first implementation auto-connected two pins only on exact world-coordinate coincidence. Live testing showed this was nearly unachievable through ordinary placement/dragging — pin offsets aren't visible before placing a symbol, so landing the exact grid alignment needed was impractical, and the feature read as broken even though the underlying check was correct. Fixed in two parts: magnetic pin-snap during placement/drag (`SchematicPageViewModel.ApplyPinMagnetSnap`) pulls a nearby *compatible* pin's row/column into exact alignment, and the user then specified the actual intended rule directly — two pins connect only when they sit on the same grid row/column *and* their outward directions point at each other (`AutoConnectDetector.AreFacingEachOther`), using each pin's `Direction` transformed by its placement's rotation/mirror (`PlacementPinGeometry.GetPinWorldDirection`: mirror reflects left/right, `180 - d`; rotation adds directly, `d + rotation`, both exact because the app only ever rotates in 90° steps). Exact coincidence is just the zero-distance case of this same rule, not a separate check — two overlapping pins pointing the same direction still do not connect. The magnet-snap was correspondingly redesigned to only pull the *axis* that determines "same line" (Y for a left/right-facing pin, X for up/down) toward a direction-compatible pin, not force full overlap, since connected pins no longer need to touch.

**Decision — connections are geometrically live, not permanent, per explicit user request:**
After the facing rule shipped, the user asked to remove the residual behavior where a wire appeared to "follow" a dragged component indefinitely regardless of where it ended up, since that implies a persistence the connection doesn't actually have. `SchematicPageViewModel.RunAutoConnect` now re-validates a moved/rotated placement's *existing* connections the same way it detects new ones: if a connection's two pins no longer satisfy `AreFacingEachOther` after the move, it is deleted (`DeleteConnectionCommand`, pushed as its own undo-stack entry, same pattern as auto-connect creation). Sliding a connected part further along its line keeps the wire (it just re-routes to the new length); moving it off that line drops the connection outright. This applies uniformly to a connection regardless of whether it was auto-created or manually drawn — deliberately not distinguishing the two, since doing so would need a new schema column for no clearly-requested benefit. A further round of feedback addressed the *visual* side of this: while a placement is being dragged, its wires are hidden entirely (`BuildWiringRenderInfo` excludes any connection touching the dragged placement's pins) rather than stretching live to the cursor, since whether the drag ends up connected is only actually decided on drop — showing a "connected" wire mid-drag was misleading regardless of how the drop was eventually resolved.

**Decision — junctions are a pure rendering-time derivation, not a stored entity:**
`JunctionDetector.FindJunctions` takes the page's already-routed connections and pin positions and returns every point where 3+ connections share an endpoint, or one connection's endpoint lands strictly inside another's segment — no `Junction` table, consistent with `Connection` carrying no geometry of its own.

**Decision — wire numbering is project-wide sequential, not the "configurable per-page-or-project" scheme Section 6.1 describes:**
Same simplification precedent as ADR-008's Designation auto-suggestion: `ProjectSession.SuggestNextWireNumber` is a plain max-existing-integer-plus-one, freely overwritable, with a "Renumber Wires" command that reassigns every connection in the project (not just the current page) in page-order — avoiding the cross-page-duplicate risk a per-page-reset scheme would introduce without also building page-vs-project scope selection UI nobody asked for yet.

**Necessary fix found while building this — `DeletePlacement` didn't account for dependent Connections:**
`Connection`'s FKs to `DevicePin` have no `ON DELETE CASCADE` (confirmed in the M1 schema). M6's `DeletePlacement` deleted a placement's exclusive `DevicePin`s directly, which never mattered before M7 because no `Connection` could reference them. `DeletePlacement` now deletes every `Connection` touching one of the placement's pins first. Undoing the delete restores the placement but not its former wires, extending ADR-007's existing recreate-not-restore acceptance to connections.

**Consequences:**
- `ProjectSession.ConnectionsChanged` was added, identical in shape to M6's `PlacementsChanged` (ADR-008 had already flagged this as the pattern to reuse) — every open `SchematicPageWindow` stays live when a wire changes anywhere in the project, not just in the window where it happened.
- Manual wire drawing still allows connecting any two pins regardless of facing/direction — the escape hatch for connections outside the simple grid-line model. Such a manually-drawn "off-rule" connection will be silently deleted if either of its placements is later moved, per the uniform re-validation decision above; this is an accepted limitation, not a distinguished case.
- Cross-page wiring (the EPLAN-style "interruption point" the user asked about after M6) remains out of scope — every interaction in M7 only offers pins on the *current* page as valid targets, so no connection spanning pages can be created yet.
- Routing stays the simple 2-point/1-bend shape Section 9 explicitly allows as a starting point ("note upgrade path") — no stub-out-in-pin-direction routing, no manual waypoint editing.

---
