# ECAD Requirements — Electrical Schematic & Cable Documentation Software

## 1. Vision

A Windows desktop application for electrical engineering documentation: multi-page machine schematics, connection management, cable assembly documentation, and report generation (connection lists, BOMs, cable manufacturing sheets). Database-first architecture: all drawings and reports are views of a single relational data model. Offline-first, local storage, single-user initially, designed to become a sellable product.

Primary reference user: an electrical engineer at a robotics company documenting machine schematics and prefabricated cable assemblies, currently using EPLAN Electric P8.

## 2. Guiding Principles

1. **Data model is the source of truth.** Symbols on schematic pages are *views* of logical devices. Reports are queries. Nothing is derived by parsing graphics.
2. **Cross-references are a data-model concern from day one.** A relay coil on page 5 and its contacts on page 12 are placements of ONE device. The schema must support multi-placement devices even if the UI arrives later.
3. **Open, editable underlying formats.** Symbols are SVG-based. Form/report layouts are declarative files (JSON or similar). Visual editors are layers on top of these formats, so power users can edit files directly and visual editors can ship later without migration.
4. **Phase discipline.** Ship a usable Phase 1 for daily internal work before productization features.

## 3. Phasing

### Phase 1 — Daily-use internal tool
- Project management (create/open/save, local SQLite)
- Parts database with custom properties, import from EPLAN parts export (CSV/XML)
- Device & connection data model, grid-based editing
- Multi-page schematic editor (canvas): symbol placement, auto-connect lines, device tagging, cross-references
- Cable objects grouping connections; per-end termination handling
- Reports: connection list, BOM/parts list, cable overview, cable manufacturing sheet (F09-style)
- PDF export of pages & reports; Excel/CSV export of report data
- Symbol library: ship IEC-style standard set; import SVG; copy-and-edit existing symbols (file-level editing acceptable in Phase 1)
- Auto wire numbering (editable)
- Undo/redo for all editing operations

### Phase 2 — Robustness & authoring tools
- In-app visual form/report layout editor
- In-app visual symbol editor (copy, modify geometry, connection points)
- Rule-based numbering schemes (configurable)
- Project templates; revision/change tracking (lightweight)
- Terminal diagrams, wiring lists (additional report types)
- DXF export (optional)

### Phase 3 — Productization
- Installer/updater, licensing/activation (offline-capable)
- Crash-safe autosave & project file integrity (backup/restore)
- Documentation, onboarding samples
- Multi-user considerations deferred until demand proven

## 4. Technology Stack

Claude Code should recommend and justify the stack. Constraints:
- Windows 10/11 desktop, installable, fully offline
- Local SQLite storage
- High-performance 2D canvas (hundreds of symbols/lines per page, smooth pan/zoom)
- First-class PDF generation with precise layout control
- Excel/CSV export
- SVG parsing/rendering for symbols
- Maintainable by one engineer with AI assistance

Candidate directions (evaluate, pick one, document why): (a) Tauri + TypeScript/React with a canvas library (Konva/PixiJS) and headless-Chromium or library-based PDF; (b) C#/.NET with Avalonia or WPF, SkiaSharp canvas, QuestPDF. Decide based on canvas + PDF quality and iteration speed.

> **Decision recorded:** see `DECISIONS.md`, ADR-001 — C#/.NET 8, WPF, SkiaSharp, Svg.Skia, QuestPDF, Microsoft.Data.Sqlite.

## 5. Data Model (core entities)

All entities live in one SQLite database per project. A separate SQLite database holds the shared parts library and symbol library (referenced, with per-project caching/copy so projects remain portable).

### 5.1 Project
- Name, metadata (customer, project number, revision, dates)
- Page structure settings (which tag segments are used)
- Numbering settings

### 5.2 Page
- Page name/number within a structured hierarchy supporting IEC 81346 segments: `=Function +Location &DocumentType /Page`
- Page type: schematic, cable drawing, report (generated), graphics
- Frame/format: A3 default, A4 supported; title block bound to project/page properties

### 5.3 Device
- Full tag per IEC 81346: `=Function +Location -DeviceTag` (segments individually stored; display configurable)
- Reference to a Part (optional but normal)
- One device → many Placements (symbol instances on pages) → enables cross-references
- Device has Pins/Terminals (from part definition or manually defined): pin name, function, optional technical data

### 5.4 Placement (symbol instance)
- Belongs to one Device, one Page; position, rotation, variant
- Symbol reference (from symbol library)
- Which pins of the device this placement exposes (e.g., coil placement exposes A1/A2; contact placement exposes 13/14) → this is the cross-reference mechanism
- Cross-reference display: each placement of a multi-placement device shows page/column references to sibling placements (relay coil lists its contacts' locations; contacts point back to coil)

### 5.5 Connection
- FromPin (Device+Pin), ToPin (Device+Pin)
- Properties: wire number (auto-assigned, editable), color (IEC color codes), cross-section (mm²), length, optional Part (wire/conductor article)
- Optional membership in a Cable (as a specific core/conductor of that cable)
- Two Connection Ends, each with:
  - **Termination (optional, toggleable per end):** termination type (ferrule, wire-end sleeve, ring lug, pin contact, tinned, none) + Part reference (e.g., specific Weidmüller ferrule) → the combination of device pin + wire can have a termination switched on or off independently per end
  - Stripping length (from termination part data or manual)
- Connection source: drawn on schematic (auto-detected from touching symbol pins via connection lines) or entered directly in the connections grid — both write to the same table

### 5.6 Cable
- Cable tag (device-like: `-W12`), Part reference (cable article), type designation, length
- Cores: defined by cable part (count, colors/numbers, cross-section) → connections are assigned to cores
- Cable end-type classification derived or manually set (e.g., FER-FER, FER-CONN, FER-COMP, CONN-CONN) → used to select the appropriate manufacturing sheet layout
- Cables appear on schematic as cable definition lines crossing connections, and/or exist purely as data

### 5.7 Part (parts library)
- Article number, manufacturer, description, order data, price fields
- Classification hierarchy (e.g., Electrical Engineering > Connections > Ferrules)
- Type-specific structured data: for cables (core count, core identification, cross-section, outer Ø), for ferrules (cross-section range, stripping length, DIN color), for devices (pin/terminal definitions, symbol default)
- **Custom user-defined properties (UDPs):** typed (text, number, value-with-unit, enum list), definable by the user, attachable to parts, devices, connections, cables. Example: `CableEndType` enum, `StrippingLength` value-with-unit
- **Import:** parts data import from EPLAN parts export files (CSV and/or XML). No drawing/project import.

> **Decision recorded:** see `DECISIONS.md`, ADR-003 — the primary import source is EPLAN's native `.edz` parts export (per-part XML, far richer than plain CSV/XML), based on inspection of a real H2L Robotics export. CSV/XML remains a secondary/fallback import path.

### 5.8 Symbol
- SVG-based geometry + metadata: connection points (position, direction, pin mapping), text placeholders (tag, cross-refs, technical data), variants (rotations/mirrors)
- Library shipped with standard IEC-style symbols; user can import SVG and duplicate+edit existing symbols

### 5.9 Form / Report Layout
- Declarative layout definition (JSON): static graphics, header/footer areas, repeating data rows (dynamic area), data field placeholders, page-break rules
- Used by the report generator; editable as files in Phase 1, visual editor in Phase 2

## 6. Functional Requirements

### 6.1 Schematic editor
- Multi-page canvas editor: place symbols from library browser (search by name/category)
- Auto-connect: orthogonal connection lines; touching a symbol pin creates a Connection in the database; deleting updates it
- Junctions/T-nodes supported; connection lines re-route when symbols move
- Device tagging: on placement, prompt/auto-assign tag using page's =Function +Location context; tag uniqueness enforced per project
- Cross-references rendered live next to multi-placement devices
- Wire numbers displayed on connections; auto-numbered sequentially per page or project (configurable), manually editable, renumber command available
- Grid snap, pan/zoom, rubber-band select, copy/paste (with tag re-assignment prompt), full undo/redo
- Cable definition line: draw across connections to assign them to a cable; core assignment dialog

### 6.2 Grid-based editing (parallel path to the canvas)
- Devices grid: create/edit devices and pins without drawing
- Connections grid: create/edit connections (from-pin, to-pin, properties, terminations) without drawing → connections created here can later be represented graphically or remain data-only
- Cables grid: manage cables, core assignments
- Bulk editing: multi-row property assignment (e.g., assign ferrule part to all selected connection ends filtered by cross-section)

### 6.3 Termination handling
- Each connection end has a termination toggle; when ON, choose termination type and part
- Filterable termination view: e.g., all ends with cross-section 0.5 mm² and termination=ferrule with no part assigned → bulk-assign part
- Stripping length auto-populated from part data, overridable
- Terminations appear in BOM (counted once — no double counting) and on cable manufacturing sheets per end

### 6.4 Reports (Phase 1 set)
All reports are generated pages within the project (placed under a document-type page segment) and exportable to PDF; underlying data exportable to Excel/CSV.
1. **Connection list:** from/to (full tags+pins), wire number, color, cross-section, terminations per end, cable/core if assigned
2. **BOM / parts list:** aggregated by article number with quantities; grouping options (per project / per location / per cable assembly); cable assemblies can be reported as modules containing their child parts without double counting terminations
3. **Cable overview:** all cables with tag, type, length, core count, from/to locations
4. **Cable manufacturing sheet (F09-style):** one sheet (or more pages) per cable: header with cable data, drawing area showing cable ends with connector/termination detail, per-core table (core id, color, from-pin, to-pin, termination each end, stripping lengths); layout variant selected by cable end-type classification (e.g., FER-FER vs FER-CONN layouts)
- Report regeneration: re-running reports updates existing generated pages without page-number collisions (report pages live in their own document-type segment)

### 6.5 Export
- PDF: any page selection or whole project, vector output, correct fonts/line weights
- Excel/CSV: every report's underlying dataset

### 6.6 Parts management UI
- Browse/search parts library, classification tree navigation
- Create/edit parts including type-specific data and UDPs
- Define UDPs (name, type, unit, enum values)
- Import wizard for EPLAN parts exports with field mapping

## 7. Non-Functional Requirements
- English UI only
- Fully offline; no network dependency for any feature
- Responsive canvas at 500+ elements per page; project sizes up to ~500 pages, ~20k connections
- Autosave / crash recovery; project file is a single folder or file that can be copied/backed up trivially
- All destructive operations undoable; database migrations versioned from v1
- Keyboard-friendly: shortcuts for placement, connection, page navigation

## 8. Explicitly Out of Scope (for now)
- EPLAN project/drawing import (parts data import only)
- PLC addressing, bus topology, panel layout / 3D, macro variant technology
- Multi-user concurrent editing, cloud sync
- Multi-language UI
- DXF/DWG export (Phase 2 candidate)

## 9. Open Questions for Implementation
- Stack decision (Section 4) — decided, see `DECISIONS.md` ADR-001
- Symbol SVG conventions: define the metadata schema (connection points, text anchors) before building the library
- Form layout JSON schema: define before report engine work
- Auto-routing sophistication for connection lines: start with simple orthogonal routing; note upgrade path
