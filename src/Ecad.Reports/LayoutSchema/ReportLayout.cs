using System.Text.Json.Serialization;

namespace Ecad.Reports.LayoutSchema;

/// <summary>
/// The declarative report/form layout schema (REQUIREMENTS.md Section 5.9): static graphics,
/// header/footer areas, repeating data rows, field placeholders, page-break rules. A ReportLayout is
/// hand-authored as a JSON file (Phase 1 — no in-app editor yet) and mapped onto QuestPDF's fluent API
/// by LayoutRenderer. DataFieldKey/DataSourceKey are dotted-path strings resolved at render time
/// against a ReportDataContext that each report Builder fills — the schema and renderer never touch
/// domain models (Connection/Cable/Part/...) directly.
/// </summary>
public sealed record ReportLayout(
    string ReportKind,
    string? Variant,
    PageSetup Page,
    HeaderFooterRegion? Header,
    HeaderFooterRegion? Footer,
    IReadOnlyList<LayoutRegion> Body,
    PageBreakRule PageBreak);

public sealed record PageSetup(
    string PaperSize,
    string Orientation,
    float MarginLeftMm,
    float MarginTopMm,
    float MarginRightMm,
    float MarginBottomMm);

public sealed record HeaderFooterRegion(float HeightMm, IReadOnlyList<LayoutRegion> Content);

/// <summary>OneEntityPerPage (the manufacturing sheet) is enforced one level up — one render call per
/// Cable — not inside LayoutRenderer. MaxRowsPerPage is advisory; QuestPDF's Table() already paginates
/// natively when content overflows a page.</summary>
public sealed record PageBreakRule(int? MaxRowsPerPage, bool OneEntityPerPage);

/// <summary>One region of a report body/header/footer. Kept a closed polymorphic hierarchy (not an
/// interface) so ReportLayoutLoader can deserialize a JSON template's Body array via System.Text.Json's
/// built-in "kind" type discriminator.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(StaticTextRegion), "StaticText")]
[JsonDerivedType(typeof(FieldRegion), "Field")]
[JsonDerivedType(typeof(DrawingAreaPlaceholder), "DrawingArea")]
[JsonDerivedType(typeof(RepeatingTableRegion), "Table")]
public abstract record LayoutRegion;

/// <summary>Fixed text/graphics that never change per report run (e.g. a title-block label).</summary>
public sealed record StaticTextRegion(string Text, string FontStyle, float FontSizePt) : LayoutRegion;

/// <summary>A single "Label: {value}" placeholder resolved against the report's scalar data (e.g. a
/// cable manufacturing sheet's header fields).</summary>
public sealed record FieldRegion(string Label, string DataFieldKey, float? WidthMm) : LayoutRegion;

/// <summary>A named region LayoutRenderer dispatches to bespoke drawing code (CableEndDrawer) rather
/// than generic table/field primitives — the manufacturing sheet's cable-end/connector diagram.</summary>
public sealed record DrawingAreaPlaceholder(string PlaceholderKey, float HeightMm) : LayoutRegion;

/// <summary>A repeating table bound to a named row-source in the report's data context (e.g.
/// "Connections", "Cores", "Parts").</summary>
public sealed record RepeatingTableRegion(string DataSourceKey, IReadOnlyList<TableColumn> Columns) : LayoutRegion;

public sealed record TableColumn(string Header, string DataFieldKey, float WidthMm, string Align);
