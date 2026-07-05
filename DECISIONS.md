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
