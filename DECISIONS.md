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

## ADR-010: M8 Grid-Based Editing — Cable.ProjectId Migration, Bare Devices, Delete-Guard Asymmetry, No Undo/Redo

**Status:** Accepted (2026-07-07)

**Context:**
M8 builds the "parallel path to the canvas" Section 6.2 asks for: a Grid Editor window (one `TabControl`, three tabs) that creates/edits Devices, Connections, and Cables directly, without drawing anything. Confirmed with the user before starting: M8 covers CRUD-grid mechanics only, for data the schema already supports — the deeper termination workflows in Section 6.3 (filterable bulk-ferrule-assign, auto-populated stripping length, end-type-classification logic) stay M9's scope, even though the `ConnectionEnd` table and its termination columns already exist and are already populated (termination off) by every `ProjectSession.CreateConnection` call since M7. The Connections grid deliberately never surfaces those columns.

**Decision — `Cable` gained a `ProjectId` column via migration `0003`, rather than scoping the Cables grid through a join on `Connection`:**
`Cable` had never been touched by `ProjectSession` before M8 and had no `ProjectId` column at all. A join-through-`Connection` approach for "all cables in this project" was considered and rejected: Section 5.6 explicitly allows a Cable to exist as pure data with zero Connections assigned yet, and such an orphan cable must still be listed by a project-scoped grid — a join would silently drop it. The column is nullable (no real `Cable` rows existed anywhere before this, since `CableRepository.InsertCable` had never been called from any wired-up code path, so there was nothing to backfill).

**Decision — the Devices grid can create a "bare" Device with no Placement at all:** *(superseded two days later by ADR-011 — the grid no longer creates Devices at all; kept here for the historical record.)*
`ProjectSession.CreateBareDevice` inserts a `Device` row only — new territory, since `PlaceSymbol` (M5/M6) always creates Device+DevicePin+Placement+PlacementPin together as one unit. This is the concrete meaning of Section 6.2's "create devices... without drawing": a Device that exists purely as data until (if ever) placed on a page. `CanDeleteDevice`/`DeleteDevice` mirror this — a Device is only grid-deletable while it has zero Placements; a placed Device must still go through `DeletePlacement`'s existing placement-vs-device bookkeeping (ADR-008).

**Decision — deleting a referenced Cable is rejected, but deleting a referenced CableCore auto-clears the reference instead of blocking:**
`DeleteCable` throws if any `Connection.CableId` still points at it (`CanDeleteCable`/`AnyConnectionReferencesCable`) — losing an entire cable's identity out from under wires that reference it would be a large, silent surprise. `DeleteCableCore` instead calls `ConnectionRepository.ClearCableCoreReferences` first, un-assigning just the `CableCoreId` on any referencing Connection (leaving `CableId` intact) before deleting the core. The asymmetry is deliberate: a core is a small, easily-re-picked per-wire assignment, and blocking its deletion whenever any wire happened to reference it would make cores nearly undeletable in a grid workflow where wires get reassigned between cores routinely.

**Decision — M8 grid edits are not undoable; destructive actions get a confirmation dialog instead:**
`UndoRedoStack`/`IUndoableCommand` (M5) are instantiated per open `SchematicPageWindow`, and the Grid Editor must work with zero pages open — there is no natural stack to push onto, and picking an arbitrary "current" page window to host grid undo would be fragile and surprising. Non-destructive grid edits (property changes, bulk-assign) commit immediately, same as the canvas's non-delete edits; destructive ones (deleting a Device/Connection/Cable/CableCore from a grid) get a `MessageBox.Show(..., YesNo)` confirmation as the safety net in place of undo. Section 7's "all destructive operations undoable" is a real Phase 1 non-functional requirement this doesn't fully satisfy — recorded here as a stated gap rather than left silent. Follow-up path if ever wanted: a small `GridEditorViewModel`-owned `UndoRedoStack`, with command wrappers shaped like `RenameWireNumberCommand` (capture old/new value, `Do`/`Undo` both call the same `ProjectSession` setters this milestone already added).

**Consequences:**
- `ProjectSession.CablesChanged` was added, identical in shape to `PlacementsChanged`/`ConnectionsChanged` — the Grid Editor's three tabs and any future window showing Cable-derived data stay live off one shared event, same reuse-the-pattern precedent ADR-008/009 established.
- Inline Device/Connection/Cable tag and property edits in the grid are not uniqueness- or cross-reference-validated the way the canvas's dedicated dialogs (`DeviceTagDialog`, `WireNumberDialog`) are — an accepted simplification for this milestone, not a validation gap anyone has hit yet.
- `Device.PartId`, unused since M1 and explicitly deferred out of M6 (ADR-008) as "likely M8," is now written to for the first time, via both a single-row edit and the Devices tab's bulk-assign action.

---

## ADR-011: Grid Editor — Devices/Connections Are Graphics-First, Not Grid-Creatable (Amends ADR-010)

**Status:** Accepted (2026-07-09)

**Context:**
Two days after M8 shipped with "Add Device" (bare device, no Placement) and "Add Connection" (pin-picker, no drawing) creation UI, the user clarified an explicit product direction: Devices and Connections should always be *generated from the graphics*, not the other way around — for now, deliberately mimicking EPLAN's workflow, where the schematic drawing is the source of truth and list/grid views are secondary. Grid-first creation (a device or wire that then needs to somehow "become" graphics) was explicitly rejected as the wrong direction *for now*, with the user noting this could reverse later once more of the workflow exists.

**Decision — removed grid-based creation for Devices and Connections; kept it for Cables:**
- `DevicesGridViewModel` lost its "Add Device" toolbar (`NewFunction`/`NewLocation`/`NewTag`/`AddDeviceCommand`), and `ProjectSession.CreateBareDevice` was deleted outright (not deprecated-and-kept) since it had no remaining caller — a Device is now only ever created by `PlaceSymbol` (placing a symbol on a page).
- `ConnectionsGridViewModel` lost its "Add Connection" toolbar (`NewFromPin`/`NewToPin`/`AddConnectionCommand`) — a Connection is now only ever created by drawing or auto-connecting on the canvas (`ProjectSession.CreateConnection`, unchanged, still called from `SchematicPageViewModel`). The Connections grid's From/To Pin columns remain editable — that's *rewiring an existing* connection's endpoints, not creating a new one, so it stays.
- `CablesGridViewModel`'s "Add Cable" was deliberately kept: unlike Devices/Connections, there is no canvas-side mechanism to create a Cable at all yet (REQUIREMENTS 6.1's "cable definition line" is unbuilt), so grid creation is the only way to create one until that exists. This is a temporary exception, not a disagreement with the graphics-first principle — once cable-definition-line drawing is built, this should be revisited.

**Consequence — `CanDeleteDevice`/`DeleteDevice` (ADR-010) become a dormant cleanup path, not removed:**
With no way left to create a zero-Placement Device, and `DeletePlacement` already auto-deleting a Device the moment its last Placement is removed (ADR-008), a Device row with zero Placements should never actually exist going forward. `CanDeleteDevice`/`DeleteDevice` were kept anyway (not deleted) as a defensive cleanup path for that state if it's ever reached some other way (e.g. legacy data from before this change) — tested directly against a Device row inserted through `DeviceRepository` bypassing `ProjectSession`, since the public API to reach that state no longer exists.

**Consequences:**
- REQUIREMENTS.md 6.2 updated with a note recording this as a deliberate, dated decision rather than silently drifting from the written spec — the grids there now read "edit... (creation happens by placing a symbol / drawing on a page)" for Devices/Connections, with Cables called out as the one current exception.
- No schema or `ProjectSession` changes beyond deleting `CreateBareDevice` — `CreateConnection`/`GetConnectionsForDevicePin`/etc. are all unchanged and still shared with the canvas.
- Tests: removed `CreateBareDevice_HasNoPlacement`; rewrote the Device-setup steps of `UpdateDevicePart_SetsAndClearsPartId`, `BulkUpdateDevicePart_...`, and `AddDevicePin_UpdateDevicePin_RoundTrip` to use `PlaceSymbol` instead of the removed `CreateBareDevice`; `CanDeleteDevice_FalseWhenPlaced_TrueWhenBare`/`DeleteDevice_SucceedsForBareDevice` renamed and rewritten to construct their zero-Placement case via direct repository access. 136 tests passing (down one net from 137 — one test removed, none added, since this milestone only removed capability).

---

## ADR-012: Fixed a Real Crash — Device.PartId Assignment Never Actually Cached the Part Into the Project DB

**Status:** Accepted (2026-07-09)

**Context:**
Live-testing M8's Devices grid, the user hit an unhandled `Microsoft.Data.Sqlite.SqliteException: SQLite Error 19: 'FOREIGN KEY constraint failed'` from `DeviceRepository.UpdateDevicePart`, triggered via the "Bulk-assign Part to selected" button. Root cause: `Device.PartId` is a foreign key into *this project's own* `Part` table — a local cached copy, by design (ADR-003: "Project DB keeps a local cached copy of any Part... that a Device references, so a project file stays portable... without needing the shared Library DB present"; `Part.cs`'s own doc comment says the same). But M8's Devices grid populated its Part picker straight from the Library DB (`PartsLibraryViewModel`'s exact pattern) and wrote that Library-DB `Part.Id` directly into `Device.PartId` — a foreign key to a row that, in general, doesn't exist in the project's own `Part` table at all, since the two databases assign `Id`s independently. This "cache the Part locally on first reference" step was part of the design since ADR-003 (M1) but had never actually been implemented by any milestone, because M8 is the first feature that ever writes to `Device.PartId`.

**Decision — `ProjectSession.EnsurePartCached(long libraryPartId, string? libraryFilePath = null)`:**
Opens the Library DB (default `%LOCALAPPDATA%` path, overridable for tests), reads the source `Part`, resolves its Manufacturer/Supplier `Organization` into the project DB by `ExternalKey` (`Organization.Id` is just as non-portable across the two databases as `Part.Id` — `GetOrCreateOrganization`, already used by the M3 importer, does the lookup-or-insert), clears `ClassificationId`/`SourceImportBatchId` (nothing populates Classification anywhere yet — M3's own deferred-scope note — and `ImportBatch` rows are Library-DB-local and meaningless in a project cache), then calls the existing `PartRepository.UpsertByExternalKey` against the *project's* connection. Returns the resulting **project-local** `Part.Id` — the only value valid for `UpdateDevicePart`/`BulkUpdateDevicePart`. `DevicesGridViewModel.ApplyBulkAssignPart` now calls this once per bulk-assign action (not once per device) before writing the resolved id to the selection.

**Decision — the per-row Part column became read-only, backed by a new `DeviceRow` display shape:**
The original inline-editable `DataGridComboBoxColumn` bound `Device.PartId` directly against the Library `Part` list's `Id` — which silently breaks the moment the two databases' ids diverge (almost always): the combo would show blank/wrong-selected even when a Part *is* assigned, since the stored (project-local) id doesn't match any Library-sourced item's id in the dropdown. Rather than build a heavier two-way id-reconciliation layer (a converter resolving project-local-id → ExternalKey → matching Library item, both directions), the Devices grid now shows a plain read-only `PartExternalKey` text column, resolved once per row on refresh via the new `ProjectSession.GetCachedPart`. `DeviceRow` (in `DevicesGridViewModel.cs`, following `PlacementWithSymbol`'s existing precedent of a flattened read-shape rather than mutating a Core domain model with UI-only fields) carries `Id`/`FunctionSegment`/`LocationSegment`/`DeviceTagSegment`/`PartId`/`PartExternalKey`; only the tag fields stay inline-editable, Part assignment goes exclusively through "Bulk-assign Part to selected" (works identically for a 1-row selection).

**Consequences:**
- `PartRepository` gained `GetOrganization(long id)`, a single-row counterpart to the existing `GetAllOrganizations()`.
- `ProjectSession` gained a `_parts` (`PartRepository`) field, bound to the project's own connection — the first ProjectSession field for the Part-family tables, since nothing before M8 ever wrote to a project's local `Part` copy.
- Connections' and Cables' own `PartId` columns (wire/conductor article, cable article) are NOT exposed as editable grid columns today — this exact bug was only reachable through Devices, so no equivalent fix was needed there yet. If/when those become grid-editable, they'll need the same `EnsurePartCached` step.
- 4 new tests in `ProjectSessionGridEditingTests` (copy-into-project + returned id, idempotency across repeat calls, manufacturer-organization resolution, and a direct regression test reproducing the exact reported crash) — 140 tests passing total (8 Core + 58 Rendering + 74 Data, up from 136).

---

## ADR-013: Fixed a Real Bug — Devices Grid's "Delete Selected" Silently Did Nothing

**Status:** Accepted (2026-07-09)

**Context:**
Immediately after ADR-012's fix, the user reported "delete selected does not work, it leads to nothing" on the Devices tab. Root cause: `DeleteSelected` filtered the selection down to devices where `CanDeleteDevice` is true (zero Placements), then silently returned if that filtered set was empty — no dialog, no message, nothing. ADR-011 (two entries above) had already removed the only way to create a Device with zero Placements (`CreateBareDevice`), so every Device now visible in the grid necessarily has at least one Placement — meaning `CanDeleteDevice` is now, in practice, *always* false for anything a user could actually select. The button looked clickable and did absolutely nothing when clicked, with no feedback explaining why. ADR-011 had already predicted `CanDeleteDevice`/`DeleteDevice` would become a "dormant cleanup path" but didn't follow that thought through to what it does to the button's own usability.

**Decision — gate the command's CanExecute on "at least one selected item is actually deletable," not just "something is selected":**
`DeleteSelectedCommand`'s `CanExecute` changed from `HasSelection` (any selection) to a new `CanDeleteSelected` (`SelectedDevices.Any(d => _session.CanDeleteDevice(d.Id))`). The button now disables outright the moment a selection contains nothing deletable, instead of staying enabled and doing nothing on click — matching how disabled state is used everywhere else in this app (Save/AddPage/etc. via `IsProjectOpen`). For a mixed selection (some deletable, some not), the confirmation dialog now also states how many are being skipped and why ("still have placements on a schematic page... remove those placements from the canvas first"), rather than silently only acting on the deletable subset.

**Consequence — applied the identical fix to the Cables tab's "Delete Selected" pre-emptively:**
`DeleteSelectedCables` had the exact same shape (filter by `CanDeleteCable`, silently return if empty) — not yet reported broken (a fresh, unreferenced Cable IS deletable, so it isn't *always*-broken the way Devices' was), but the identical bug class was clearly present and would surface the same way the moment a user selected only Cables still referenced by a Connection. Fixed the same way: `CanDeleteSelectedCables` gates the command, and the confirmation message explains skipped, still-referenced cables.

**Consequences:**
- No schema or test changes — this is App-layer-only (ViewModels), and this project has no automated UI tests (STRUCTURE.md's existing note: "this app doesn't have any"), so verification is necessarily a live click-through, not a new xUnit test.
- General lesson for any future grid-delete feature in this app: gate the command's enabled state on "would this actually do something," not just "is anything selected" — a silently-inert enabled button is worse than a disabled one.

---

## ADR-014: Fixed a Real Crash — DataGrid's Hidden "New Row" Placeholder Broke Multi-Select

**Status:** Accepted (2026-07-09)

**Context:**
Multi-selecting rows on the Connections tab crashed with `System.InvalidCastException: Unable to cast object of type 'MS.Internal.NamedObject' to type 'Ecad.Core.Models.Connection'` from `ConnectionsGridView.OnSelectionChanged`'s `foreach (Connection connection in Grid.SelectedItems)`. Root cause: none of M8's four `DataGrid`s (Devices, Connections, Cables, Cables' Cores sub-grid) set `CanUserAddRows`, which defaults to `true` — every one of them has an invisible extra "new row" placeholder at the bottom, backed internally by a sentinel object (`MS.Internal.NamedObject`, exposed to WPF as `CollectionView.NewItemPlaceholder`), not a real row of the bound type. A multi-select gesture (Shift+Click, Ctrl+A, drag-select) that spans down to that placeholder includes the sentinel in `DataGrid.SelectedItems`, and the code-behind's `foreach (T x in SelectedItems)` implicit-casts every item — including the sentinel — to the model type, throwing.

**Decision — two-layer fix, applied identically to all four grids:**
1. `CanUserAddRows="False"` on every `DataGrid` — removes the placeholder row outright. None of these grids use the native "type into the bottom row to add" gesture anyway (Devices/Connections have no grid-based creation at all per ADR-011; Cables uses an explicit "Add Cable"/"Add Core" button, not the placeholder), so there's no feature loss.
2. Changed every selection-changed handler's `foreach (T x in SelectedItems)` to `foreach (var x in SelectedItems.OfType<T>())` — a defensive second layer, so even if `CanUserAddRows` is ever reintroduced (a future column needing native inline-add, a copy-pasted grid elsewhere), a non-`T` item in the selection is silently filtered instead of crashing the app.

**Consequences:**
- No schema, `ProjectSession`, or test changes — purely `Ecad.App` View/code-behind, and (per ADR-013's own note) this app has no automated UI tests, so verification is a live click-through.
- General lesson recorded for any future `DataGrid` added to this app: default to `CanUserAddRows="False"` unless the native inline-add placeholder row is actually the intended creation UX, and prefer `SelectedItems.OfType<T>()` over a bare `foreach (T x in SelectedItems)` regardless, since the placeholder risk exists on any `DataGrid` whose `CanUserAddRows` isn't explicitly turned off.

---

## ADR-015: Grid Delete Semantics — Devices/Cables Cascade-Delete Their Graphics; Connections Can't Be Grid-Deleted At All

**Status:** Accepted (2026-07-09)

**Context:**
Live-testing the fixes above, the user raised a deeper point about *what deleting means* for each of the three grids: Connections are created purely because two symbols' pins face each other on a page (M7's auto-connect rule) — they have no identity independent of that geometry, so a Connection shouldn't be removable as a standalone grid action at all; the only way to remove one is to change the geometry that produces it (move the symbols apart, or delete the wire on the canvas). Devices and Cables are different — they're *parts*, with their own identity independent of where they happen to be drawn, so deleting one from the grid is a legitimate action, but it should mean "delete this part, everywhere" — including removing its symbol(s) from every schematic page — not the narrower "only delete it from the grid if it happens to have no placements left" behavior ADR-011 had left in place (which, per ADR-013, had degenerated into `CanDeleteDevice` being permanently false for anything actually selectable).

**Decision — Connections tab: removed "Delete Selected" entirely, not just guarded it:**
Unlike Devices/Cables, there's no cascade to design here — a Connection is deleted by no longer satisfying the auto-connect facing rule, full stop. `ConnectionsGridViewModel.DeleteSelected`/`DeleteSelectedCommand` and the button in `ConnectionsGridView.xaml` are gone; `SelectedConnections` and the bulk-color/cross-section actions remain (those edit an existing connection's data, they don't touch whether it exists).

**Decision — `ProjectSession.DeleteDeviceCascade(long deviceId)`: deletes a Device and every Placement of it, on every page, in one action:**
Refactored `DeletePlacement`'s body into a private `DeletePlacementCore` (identical logic, no event-raising) so both the existing public `DeletePlacement` (unchanged behavior/tests) and the new `DeleteDeviceCascade` can share it. `DeleteDeviceCascade` fetches every Placement Id for the Device (`PlacementRepository.GetPlacementIdsForDevice`, new) and runs each through `DeletePlacementCore` — which already deletes each placement's dependent Connections and exclusive DevicePins, and (via its own existing logic) deletes the Device row itself once the last Placement is gone — then raises `PlacementsChanged`/`ConnectionsChanged` at most once each for the whole batch, not once per placement. `DevicesGridViewModel.DeleteSelected` now always calls this (no more filtering by "is this device already placement-less") and its confirmation dialog says explicitly that placed symbols will be removed too.

**Consequence — `ProjectSession.CanDeleteDevice`/`DeleteDevice` (ADR-010/ADR-011) removed outright:**
`DeleteDeviceCascade` supersedes them for every case the grid needs (zero-Placement devices still just get a plain delete internally); with the Devices grid as their only caller now gone, they had zero remaining callers — deleted rather than left as unreachable dead code, per this project's own "delete what's actually unused" convention. Tests exercising the old zero-Placement-only behavior were replaced with tests for `DeleteDeviceCascade`'s cascade (multi-page, dependent-connections, single-event-per-batch) and its zero-Placement fallback.

**Consequences:**
- 3 tests replaced (removed the old `CanDeleteDevice`/`DeleteDevice`-specific tests, added `DeleteDeviceCascade_RemovesDeviceAndAllPlacementsAcrossPages`, `DeleteDeviceCascade_DeletesDependentConnections_RaisesEachEventOnceForTheWholeBatch`, `DeleteDeviceCascade_WithZeroPlacements_StillDeletesTheDevice`) — net test count unchanged at 140.
- Cable deletion is untouched — it was already "fine to delete" in the user's own words, since `CanDeleteCable`/`DeleteCable`'s reject-if-referenced guard (ADR-010) already matches the "parts have independent identity, deleting one is a real action" model; Cables have no canvas-drawn symbol yet to cascade-remove (Section 6.1's cable-definition-line feature is still unbuilt).

---

## ADR-016: M10 Application Shell — One Tabbed Window Replaces Per-Document Floating Windows

**Status:** Accepted (2026-07-10)

**Context:**
With M9 confirmed, every window in the app was still an independent floating `Window`: one `SchematicPageWindow` per open page (a static `Dictionary<long, SchematicPageWindow>` registry, ADR-008/M7's interruption-points follow-up), a singleton `GridEditorWindow` (ADR-010), and unlimited `PartsLibraryWindow`/`SymbolBrowserWindow` instances. The user asked for a real application shell instead ("it cannot be windows floating on top of each other"), plus recent-files/auto-reopen-on-launch behavior, which had no mechanism at all (confirmed by direct search — no settings file, no MRU list). This was scoped as a new M10, inserted ahead of the existing M10/M11/M12 (Reports/Export/Hardening), which shift to M11/M12/M13 — the same "insert a milestone, renumber the rest, note it as a deliberate reorder" precedent this log already uses for other reprioritizations.

**Decision — one `MainWindow`, documents as tabs, resolved by implicit `DataTemplate`:**
`DocumentTabViewModel` (`Header`, `Content` = the actual per-document ViewModel instance, `IsProjectScoped`, `PageId` for find-existing lookups) lives in an `ObservableCollection<DocumentTabViewModel> OpenTabs` on `MainViewModel`, bound to a `TabControl` in `MainWindow.xaml`. Each tab's visual content is resolved via implicit `DataTemplate`s keyed by ViewModel type, declared as `MainWindow.xaml` resources (`SchematicPageViewModel` -> `SchematicPageView`, `GridEditorViewModel` -> `GridEditorView`, `PartsLibraryViewModel` -> `PartsLibraryView`, `SymbolBrowserViewModel` -> `SymbolBrowserView`) — one `ContentPresenter`, many possible Views, no manual view-locator. Every former `*Window` was extracted into a same-named `*View` `UserControl` and the old `Window` file deleted outright, not kept alongside.

**Decision — a real WPF gotcha found the hard way: implicit-`DataTemplate` Views get their `DataContext` assigned AFTER construction:**
`SchematicPageWindow`'s original `RedrawRequested += () => SkiaCanvas.InvalidateVisual()` wiring ran in the constructor, which worked fine as a `Window` (constructed with its `DataContext` already set) but silently never fired once extracted into `SchematicPageView` — a View hosted via an implicit `DataTemplate` inside a `TabControl` gets templated (and its `DataContext` bound) only after construction completes. Live click-through immediately caught this: a newly-opened tab showed a blank canvas until a stray click on it forced a repaint. Fixed by moving the `RedrawRequested` subscription into a `DataContextChanged` handler (unsubscribing the old ViewModel, subscribing the new one) instead of the constructor. This alone wasn't sufficient for the *first* paint, though — nothing in `SchematicPageViewModel`'s constructor raises `RedrawRequested`, and `SKElement` doesn't reliably self-paint the moment it's freshly templated into a newly-selected tab regardless — so an explicit `SkiaCanvas.InvalidateVisual()` call was added both in the `DataContextChanged` handler and in `Loaded` (whichever fires second is the one with an actually-laid-out `SKElement` to invalidate). Recorded here as the concrete gotcha since it will recur for any future ViewModel-typed View hosted the same way — `GridEditorView`/`PartsLibraryView`/`SymbolBrowserView` didn't need this workaround at all, since none of them paint through a manually-invalidated `SKElement`; every control in them is an ordinary WPF-bound `DataGrid`/`ListView`/`ItemsControl` that WPF's own binding system already repaints correctly regardless of when `DataContext` lands.

**Decision — singleton tabs are found-or-created by ViewModel type, not a separate registry:**
`GridEditorView`'s tab (project-scoped, one per open project) and `PartsLibraryView`/`SymbolBrowserView`'s tabs (project-*independent*, `IsProjectScoped = false`, survive `CloseCurrentSession` — matching the old windows, which never held a `ProjectSession` reference) are each found via `OpenTabs.FirstOrDefault(t => t.Content is TTypeViewModel)` rather than a second static dictionary — simpler than `SchematicPageWindow`'s old per-page keyed registry since there's only ever one instance of each, so the ViewModel's own type is a sufficient key. Schematic pages keep their own `PageId`-keyed lookup (`OpenOrFocusPageTab`) since there can be many of those simultaneously.

**Decision — recent files became "auto-reopen the last project," not a full MRU menu:**
The user's actual ask was "when I open the program I get recent files, and automatically open the latest project... but you can still close project, but if not explicitly closed then it should open with program, and open the first page of the project" — read as one behavior (auto-reopen-unless-explicitly-closed), not a literal multi-entry Recent Files submenu, which was deliberately not built (milestone-scope-discipline precedent, same as M5's rename-vs-symbol-editor clarification). New `AppSettingsStore` (`src/Ecad.App/Services/`) persists `LastOpenedProjectPath`/`WasExplicitlyClosed` as a small JSON file at `%LOCALAPPDATA%\Ecad\settings.json`, mirroring `LibraryDatabase.DefaultFilePath`'s path idiom (JSON instead of SQLite, since this is plain key-value settings, not relational data). `CloseProject` sets `WasExplicitlyClosed = true` before saving; `NewProject`/`OpenProjectFromPath`/`SaveAs` all set it back to `false`. `MainViewModel.TryAutoReopenLastProject()` is called once from `MainWindow`'s `Loaded` event — deliberately not the constructor, since `Application.Current.MainWindow` isn't reliably set yet inside a `Window`'s own constructor, and any dialog opened during auto-reopen needs to correctly own itself against the main window. Guards with `File.Exists` before calling `ProjectSession.Open`: `Microsoft.Data.Sqlite` silently creates an empty file at a missing path rather than throwing, so an unchecked stale path would otherwise leave a junk `.ecad` file behind. Any other failure during auto-reopen degrades to "No project open" plus a status message, never an unhandled exception.

**Decision — rubber-band multi-select replaces right-drag-to-pan; panning moves to middle-click-drag:**
*(The right-drag-to-select mapping below was superseded a day later by ADR-017: left-click-drag now
does the rubber-band select, and right-click opens a context menu instead. The `HitTestRect`/
`RubberBandRenderInfo`/`CompositeCommand`/group-drag mechanics described here are unaffected — only
which mouse button triggers the marquee changed.)*
A later follow-up request in the same M10 window. `PlacementHitTester.HitTestRect` (AABB intersection against a world-space rectangle) is deliberately not rotation-aware, unlike the existing point-based `HitTest` — a precise rotated-rect-vs-rect intersection isn't worth the complexity for a marquee select, where "roughly overlaps" is what a user expects. `SchematicCanvasRenderer.Render` gained a `RubberBandRenderInfo?` param (drawn as a dashed, translucent-filled rectangle) and its `selectedPlacementId: long?` param became `selectedPlacementIds: IReadOnlyCollection<long>` so the highlight loop can mark multiple placements at once. New `CompositeCommand : IUndoableCommand` (`src/Ecad.App/Canvas/Commands.cs`) wraps a list of commands so a multi-select group move or delete undoes/redoes as one atomic step (`Undo` runs the wrapped list in reverse, in case one command's undo depends on state an earlier one set up — e.g. `DeleteCommand`'s device-recreation). `SchematicPageViewModel` unifies single- and group-drag through one mechanism, `_dragGroupOriginalPositions: Dictionary<long,(X,Y)>` — a single-item drag is just the `Count == 1` case, so `HandleMouseMove`/`HandleLeftButtonUp` don't need two separate code paths. A group drag snaps the shared delta as one (keeping every member's relative offset exactly preserved) rather than snapping each member independently, which would let a group drift apart from itself; per-item magnetic pin-snap (ADR-009) is correspondingly kept only for genuine single-item drags. Confirmed with the user via `AskUserQuestion` before building: multi-select supports group Delete and group Move (not Rotate — `R` stays single-select-only), and each new rubber-band drag replaces the prior selection rather than adding to it (no Ctrl+click additive selection).

**Consequences:**
- `SchematicPageWindow.xaml(.cs)`, `GridEditorWindow.xaml(.cs)`, `PartsLibraryWindow.xaml(.cs)`, `SymbolBrowserWindow.xaml(.cs)` are all deleted — every document type in the app is now a `UserControl` hosted by `MainWindow`'s single `TabControl`, no floating windows remain except modal dialogs (`NewProjectDialog`, `AddPageDialog`, `DeviceTagDialog`, `WireNumberDialog`).
- `SchematicPageViewModel.OwnerWindow` now always resolves to the single `MainWindow`, set once when a page's tab is created, instead of being set per-window.
- Any future ViewModel-typed View added to this app's `MainWindow.TabControl` needs its own implicit `DataTemplate` entry; if it drives a manually-painted `SKElement` (or any control that doesn't self-invalidate off WPF's own binding/property-changed plumbing), it needs the `DataContextChanged`-plus-`Loaded` `InvalidateVisual` pattern this ADR documents — every purely WPF-bound control (`DataGrid`/`ListView`/`ItemsControl`) does not.
- No `Ecad.Core`/`Ecad.Data`/`Ecad.Rendering` schema changes anywhere in this milestone except the pure-rendering `SchematicCanvasRenderer`/`PlacementHitTester` additions above — this is entirely `Ecad.App` View/ViewModel restructuring, plus one new `Ecad.App.Services.AppSettingsStore`. Remaining from the original plan: Phase 4 (a docked device-properties panel replacing the modal `DeviceTagDialog` for the *rename* path only — new-placement and wire-rename dialogs stay modal, since a brief blocking prompt mid-placement is normal CAD UX and wasn't actually what was asked to change).

---

## ADR-017: M10 Follow-ups — Left-Click Selects / Right-Click Context Menu, Direction-Aware Rubber-Band, Mouse Capture, and the Docked Device Panel (Amends ADR-016)

**Status:** Accepted (2026-07-11)

**Context:**
Three more rounds of live pointer-model feedback on ADR-016's shell, plus the last remaining Phase 4 piece, all landed in the same follow-up window — recorded together since they're all M10 continuations.

**Decision — left-click drag is the rubber-band select; right-click opens a context menu (supersedes ADR-016's right-drag-to-select mapping):**
The user asked for left-click to do the selecting, with right-click freed up to "display options" — confirmed via `AskUserQuestion` that this meant building an actual context menu now, not just vacating the button. The naive approach (treat every left-button-down-on-empty-space as an instant rubber-band start) would have broken "click a wire to select it," since `PlacementHitTester.HitTestRect` only tests placements, never wires — a zero-size rectangle at a wire's location would find nothing. Instead, `HandleLeftButtonDown`'s existing wire-hit-test-or-clear logic on empty space runs exactly as before, and additionally *arms* a pending rubber-band (`_isRubberBandArmed`, records the down-position); `HandleMouseMove` only promotes armed to actively-selecting (`_isRubberBandSelecting`) once the drag crosses a small pixel threshold (`RubberBandDragThreshold`, same idea as `SchematicPageView`'s existing palette drag-start threshold). A stationary click never crosses that threshold, so the wire-selection/deselect outcome already decided at mouse-down stands unchanged; an actual drag transitions into the marquee and `HandleLeftButtonUp` finalizes it instead of falling through to the placement-drag-commit logic. Right-click (`HandleRightClick`) selects whatever's under the cursor (placement, preserving an already-active multi-selection if the target is a member of it; else a wire; else leaves the current selection untouched) without starting any drag, then WPF's native `ContextMenu` — declared on the `SKElement` in XAML, opening automatically on the matching mouse-up — shows Rotate/Delete/Undo/Redo. A `ContextMenu` is a separate popup, not part of the main visual tree, so it doesn't inherit `SkiaCanvas`'s `DataContext` automatically; bound via `DataContext="{Binding PlacementTarget.DataContext, RelativeSource={RelativeSource Self}}"` instead.

**Decision — Delete/Rotate become commands shared by the keyboard and the context menu, not duplicated logic:**
`HandleKeyDown`'s inline `Key.Delete`/`Key.R` branches were extracted into `DeleteSelectionCommand`/`RotateSelectionCommand` (`[RelayCommand(CanExecute = ...)]`), so both the `Delete`/`R` keys and the new context-menu items call the exact same method and share one enabled/disabled state — a menu item grays out precisely when the shortcut would be a no-op (e.g. Rotate while a multi-selection is active, since `SelectedPlacementId` stays null then).

**Decision — direction-aware rubber-band, AutoCAD/EPLAN-style window vs. crossing select:**
A follow-up ask: dragging left-to-right should require a placement to be *fully enclosed* to select it ("window"); dragging right-to-left should only require *touching* the rectangle ("crossing"). `PlacementHitTester.HitTestRect` gained an optional `requireFullContainment` parameter (2 new tests); `FinishRubberBandSelection` derives the mode from the drag's own screen-space direction (`_rubberBandCurrentScreen.X >= _rubberBandStartScreen.X` ⇒ window, else crossing) — screen and world X share the same ordering since the viewport transform is a uniform pan/zoom with no axis flip, so comparing screen coordinates is equivalent to comparing world coordinates here.

**Decision — the canvas captures the mouse during any drag, with a defensive cancel if capture is lost:**
Immediately after the above, the user reported that dragging (rubber-band or an object) past the canvas's edge froze until the cursor wandered back in. Root cause: WPF stops delivering `MouseMove`/`MouseUp` to an element the instant the cursor leaves its bounds, unless that element has explicitly captured the mouse. Fixed with `SkiaCanvas.CaptureMouse()` on left/middle button-down and `ReleaseMouseCapture()` on the matching button-up — every drag now keeps tracking regardless of where the cursor physically is, matching EPLAN's own drag behavior, without needing to literally clamp the OS cursor to the canvas's bounds (a heavier, more invasive fix than the actual reported symptom needed). Capture can still be taken away by something outside this app's control (e.g. focus stolen by another window mid-drag) — without handling that, an in-progress drag would be stuck waiting for a `MouseUp` that will never arrive. `SchematicPageViewModel.CancelActiveDrag()`, wired to the `SKElement`'s `LostMouseCapture` event, resets every drag-related flag and reverts a placement drag back to its pre-drag position (the same "put it back" state `HandleLeftButtonUp`'s own no-op case already uses) — a clean cancel, not a stuck or half-committed state. Calling this from our own intentional `ReleaseMouseCapture()` (which also raises `LostMouseCapture`) is harmless: by then `HandleLeftButtonUp`/`HandlePanEnd` have already run and cleared the relevant state, so `CancelActiveDrag()` finds nothing active to cancel.

**Decision — Phase 4: the docked device panel, completing M10:**
Built exactly as ADR-016 originally scoped it: `SchematicPageViewModel` gained the docked-panel state directly rather than a new composition-layer VM class (`IsDevicePanelOpen`, `DevicePanelFunction`/`Location`/`Designation` with a live `DeviceTag` preview recomputed on every keystroke, `DevicePanelValidationText`, `ApplyDeviceEditCommand`/`CloseDevicePanelCommand`). `HandleDoubleClick`'s rename-an-existing-placement branch changed from "construct `DeviceTagDialog`, `ShowDialog()`, read the result back" to "populate this state, `IsDevicePanelOpen = true`" — new-placement's tag prompt (`PlaceSymbolAt`) and wire-rename (`WireNumberDialog`) are untouched, exactly the scope boundary ADR-016 drew. `SchematicPageView` gained a collapsible `Border` docked to the canvas's right edge, `Visibility` bound through a `BooleanToVisibilityConverter`, visible only while the panel is open — non-modal, so the canvas stays interactive underneath it. `DeviceTagDialog`'s rename-mode constructor (and the `_excludingDeviceId` field it was the sole source of, always `null` once removed) had zero remaining callers once this landed — deleted outright rather than left as dead code, per this project's established "delete what's actually unused" convention (ADR-011/ADR-015); the dialog's surviving placement-mode constructor and its `DeviceCombo` picker are unaffected.

**Consequences:**
- M10 (Phases 1-4, plus every pointer-model follow-up above) is now complete.
- ADR-016's original "right-drag selects, right-click therefore isn't available for anything else" mapping is gone — right-click is now a general-purpose context-menu button for this canvas, and any future canvas action that needs a right-click-driven UI (a differently-scoped menu, a submenu) should extend the existing `ContextMenu` in `SchematicPageView.xaml` rather than reintroducing a right-drag gesture.
- The armed/threshold pattern (`_isRubberBandArmed` → `_isRubberBandSelecting` once a pixel threshold is crossed) is now the established way to let a single mouse-down decide between "immediate click behavior" and "drag behavior" without knowing in advance which the user intends — the same shape `SchematicPageView`'s palette-drag-start-threshold already used, now proven at the ViewModel layer too.
- Mouse capture (`CaptureMouse`/`ReleaseMouseCapture`/`LostMouseCapture`) is now the established pattern for any interactive drag on this canvas; a future drag-based feature that doesn't go through `HandleLeftButtonDown`/`HandlePanStart` would need to capture/release/cancel the same way to avoid the same "gets stuck at the canvas edge" bug.
- No `Ecad.Core`/`Ecad.Data` changes; `Ecad.Rendering`'s only change is `PlacementHitTester.HitTestRect`'s new optional parameter (backward-compatible default `false`, so the earlier crossing-only tests are unaffected). 155 tests passing total (153 + 2 new `HitTestRect` full-containment tests).

---

## ADR-018: M11 Connection Definition Points — Explicit Placement Replaces Auto-Numbering, Then Redesigned as an Independent Entity

**Status:** Accepted (2026-07-11/12)

**Context:**
M7 assigned every `Connection` a sequential wire number automatically and immediately on creation, always drawn on the canvas at a crude route-midpoint. The user asked to replace this with an EPLAN-style workflow instead, before starting the (also-deferred, ADR-011) cable-definition-line feature: a wire shows nothing until the user explicitly places a "definition point" — a diagonal tick carrying wire number/color/cross-section. Planned via `EnterPlanMode` with parallel research and four `AskUserQuestion` clarifications (marker-removal clears data entirely; auto-suggested number is pre-filled but editable; the on-canvas label shows all three fields; placement is a dedicated toolbar tool, not double-click).

**Decision — initial build stored the position as a route-relative fraction, not an absolute point:**
A wire's route (`OrthogonalRouter.Route`) is recomputed fresh from live pin positions on every render, with no stored geometry (ADR-009) — so the definition point's position was stored as `Connection.DefinitionPointPositionT`, a 0..1 fraction of the route's arc length (new `RouteMath.PointAtT`/`ProjectToT`), reasoned to "stay sensibly placed" as the route re-shaped. `SetDefinitionPointCommand`/`MoveDefinitionPointCommand` covered place/edit/remove/drag-reposition-on-the-same-wire/drag-relocate-to-a-different-wire as one command shape, matching `MoveCommand`/`RotateCommand`'s existing before/after-pair convention.

**Decision — extended ADR-015's "no independent connection identity" from the Grid Editor to the canvas (a real bug fix):**
Live testing found that double-clicking a bare wire could still select the underlying `Connection` (via the click portion landing on the old wire-hit-test-selects-it path) and let Delete remove the whole wire — directly contradicting the point of the new feature. Removed wire/connection selectability from the canvas entirely: `SelectedConnectionId` was deleted outright, replaced with `SelectedDefinitionPointIds` (keyed by the connection owning the point, at this stage) as the only thing a click/right-click/rubber-band drag can select on a wire. `DeleteSelection` was rewritten so Delete only ever clears a definition point's data (or deletes a placement) — never a connection.

**Decision — root-cause redesign: `DefinitionPoint` became a fully independent entity, mirroring `Placement`:**
The user then reported the actual motivating bug directly: moving *any* symbol connected to a defined wire made its definition point silently disappear. Tracing `SchematicPageViewModel.RunAutoConnect` found the real cause — it deletes and recreates the `Connection` row for any pin pair whose facing test fails after a move (i.e. nearly every free drag with a perpendicular component), and the definition point's data lived on that now-deleted row. This was a genuine architecture flaw, not a patchable symptom: anything living on a `Connection`'s own columns is exactly as ephemeral as the row itself. Redesigned `DefinitionPoint` as its own table — own `Id`, `PageId`, absolute `X`/`Y` (no longer route-relative), and an optional nullable `ConnectionId` with `ON DELETE SET NULL` — so it survives its attached connection being deleted/recreated, detaching (not disappearing) when that happens. Migration `0005` created the table (backfilling existing route-relative points as an approximate absolute position, same "reasonable, not exact" precedent `0004`'s own `0.5`-midpoint backfill set) and dropped the old `Connection.DefinitionPointPositionT` column; `0004`'s own migration became superseded scaffolding kept only for migration-history continuity. `WireNumberDialog`/`RenameWireNumberCommand` were deleted outright once `DefinitionPointDialog`/the new command set had zero remaining callers pointing at them.
Confirmed via `AskUserQuestion` before building: placement should work in free space too (not require snapping onto a wire — a definition point can be a genuinely free-floating symbol now), and Grid Editor's Connections tab should treat Color/Cross-section as read-only for a row with an attached definition point ("set via canvas"), the exact same treatment `WireNumber` already had — mirrored one-way from the `DefinitionPoint` onto the `Connection`'s own columns so Grid Editor/Terminations (which read those columns directly) keep working completely unchanged.

**Decision — two further live-testing follow-ups, applied uniformly:**
A definition point wasn't snapping to the grid while being dragged at all (fixed — the drag handler had never applied `Viewport.SnapToGrid`). Per direct request, a definition point's tick gained a `RotationDegrees` field (rotatable via the `R` key, 90° per press, the same convention already used for symbols) and switched its default (unselected) color from near-black to red, DodgerBlue still reserved for the selected-highlight state.

**Consequences:**
- `Connection` no longer carries any definition-point-related column at all; `WireNumber`/`Color`/`CrossSectionMm2` remain on it purely as a mirror target for an attached `DefinitionPoint`, or plain grid-editable data for a connection with none.
- The tick-glyph-drawing code (`SchematicCanvasRenderer.DrawDefinitionPointGlyph`) was written to be reusable by any future point-like marker needing the same diagonal-tick-plus-label visual — reused directly by cable line crossings in ADR-019 below, including the rotation transform.
- The "independent entity + `ON DELETE SET NULL`" shape is now the established pattern for any future canvas marker whose lifecycle must not be tied to a `Connection`'s own churn — applied immediately to `CableLineCrossing` in ADR-019.
- Migrations `0004`→`0007` cover this arc: `0004` (original `DefinitionPointPositionT` column, now superseded), `0005` (the `DefinitionPoint` table redesign), `0007` (adds `RotationDegrees`). `0006` is ADR-019's `CableLine`/`CableLineCrossing` tables, built in between.
- 191 tests passing by the end of this arc (up from 155 at M10) — `Ecad.Data.Tests` gained coverage for place/move/attach/detach/delete and the delete-the-connection-directly orphaning regression case; no UI/canvas-interaction automated tests exist for this app (established pattern), so the interaction itself was verified through several rounds of live click-through and fixes.

---

## ADR-019: M11 Cable Definition Lines — Draw-Across-Wires Auto Core Assignment (Resolves ADR-011's Deferral)

**Status:** Accepted (2026-07-12)

**Context:**
ADR-011 explicitly deferred cable creation off the canvas: "there is no canvas-side mechanism to create a Cable at all yet... this is a temporary exception, not a disagreement with the graphics-first principle — once cable-definition-line drawing is built, this should be revisited." REQUIREMENTS 6.1's entire spec for the feature was one line: "draw across connections to assign them to a cable; core assignment dialog." Chosen as the next feature per the user's own stated ordering, immediately after ADR-018's definition-point work landed. `AskUserQuestion` confirmed a straight-segment gesture (not a multi-point polyline); a follow-up clarification from the user then reshaped the whole design: no upfront cable/core setup should be required at all — draw the line, type just a cable name, and every crossed wire gets a core automatically, with no separate per-wire picker dialog.

**Decision — `CableLine`/`CableLineCrossing` reuse ADR-018's just-learned independent-entity lesson:**
`CableLine` (own `Id`, `PageId`, absolute `X1,Y1,X2,Y2`, `CableId`) never stores a wire's route, only its own fixed geometry — drawn every frame regardless of what it currently crosses. `CableLineCrossing.ConnectionId` is `ON DELETE SET NULL`, not `CASCADE`, for the exact same reason `DefinitionPoint.ConnectionId` is: if a crossed wire's `Connection` is deleted elsewhere (an unrelated symbol move triggering auto-connect), the crossing survives as an orphan rather than silently disappearing along with the line's other crossings — the identical failure mode ADR-018 fixed, proactively avoided here instead of needing its own bug report first. A new `SegmentIntersection` primitive (`Intersect`, `IntersectRoute`) was needed — confirmed nothing like segment/polyline crossing existed anywhere in `Ecad.Rendering` (`RouteMath`/`WireHitTester` are both point-to-polyline proximity only).

**Decision — no upfront cable setup; the canvas becomes the normal way core-to-wire assignments are made:**
`ProjectSession.DrawCableLine` finds-or-creates a `Cable` by a trimmed, case-insensitive `Tag` match (a new `SuggestNextCableTag`, same trailing-digit-increment convention as `SuggestNextWireNumber`) and auto-creates a sequentially-numbered `CableCore` (no `Color`/`CrossSectionMm2` yet — filled in later via the unchanged Grid Editor) for each newly-detected crossing, mirroring the assignment onto `Connection.CableId`/`CableCoreId` via the *already-existing* M8 `UpdateConnectionCable` — no new write path needed there. A wire already assigned to a *different* cable is skipped, never silently overwritten; a wire already crossed by *this same* line is left alone (idempotent re-draws/re-edits). Crossing detection is an explicit, triggered action (draw / drag-drop / re-edit the line), never continuous — the same scope limit `RunAutoConnect` already accepts for wire auto-connect, not a per-frame re-evaluation.
This is the actual resolution of ADR-011's deferred note: the Grid Editor's Cables tab is **unchanged** — it remains how a core's `Color`/`CrossSectionMm2`/type gets filled in after the canvas creates it, and the only way to create a Cable with zero wires assigned (pure data, per REQUIREMENTS 5.6) — Grid-based creation was never actually in tension with the graphics-first principle for Cables specifically, since a Cable's own data lifecycle is independent of any one drawn line, unlike a Device/Connection which are always visually generated.

**Decision — four rounds of live-feedback refinement, all applied the same day:**
1. Dragging either endpoint independently extends/shrinks the line (not just whole-line translate) — same screen-pixel-radius hit-test convention as a definition point's own tick.
2. The crossing marker switched from a plain circle to the identical red diagonal-tick-plus-label glyph a wire definition point uses (`DrawDefinitionPointGlyph`, now shared by both), for visual consistency between the two "something was assigned here" concepts.
3. Each crossing became independently selectable (click/right-click/rubber-band, `SelectedCableLineCrossingIds`) and rotatable (`R` key, extending `RotateSelection`'s existing single-placement/single-definition-point cases with a third), and double-click-editable via a new `CableCoreDialog` (core number/color/cross-section, with a uniqueness check against the cable's other cores) — the exact same selection/rotation/property-edit shape ADR-018 just built for wire definition points, extended onto cable crossings per direct request. Grid Editor gained a parallel `ConnectionIdsWithCableLineCrossing` read-only guard, identical in shape to `ConnectionIdsWithDefinitionPoint`.
4. A grid-snap bug in whole-line dragging — computing its move offset from the raw, unsnapped cursor position at drag-start rather than a snapped anchor position — was found and fixed, the same bug class (and same fix shape: snap the *candidate* anchor position, then derive a grid-aligned delta from that, matching the existing placement-group-drag trick) already fixed once for definition points in ADR-018.

**Consequences:**
- Migration `0006` (`CableLine`/`CableLineCrossing` tables) sits between ADR-018's `0005` and `0007` in the same overall migration arc.
- Deleting a `CableLine` clears every live crossing's mirrored `Connection.CableId`/`CableCoreId` but deliberately leaves the `CableCore` rows it created behind (orphaned, harmless) rather than cascading their deletion — matching ADR-010's existing "a core is cheap, easily re-picked" precedent.
- Drawing a line that crosses zero wires is cancelled outright with a status message — unlike a definition point, a cable line's entire purpose is crossing wires, so a zero-crossing line was judged not worth keeping as a free-floating marker.
- Explicitly out of scope for this pass (stated up front, not silently dropped): multi-point/bent cable lines, a per-crossing manual core-picker dialog (fully automatic instead), rubber-band inclusion was in fact added in the follow-up round above so this line item from the original plan is superseded, and auto-populating a core's `Color`/`CrossSectionMm2` from a Cable's linked `Part` (no data source wired up for that anywhere yet, same gap already noted for Terminations).
- 196 tests passing by the end of this arc (up from 191) — new `SegmentIntersectionTests`/`CableLineHitTesterTests`, and `Ecad.Data.Tests` coverage for core auto-numbering/idempotent re-draw/conflict-skip/mirror-clear-on-delete/re-homing/rotation/core-edit-uniqueness.

---
