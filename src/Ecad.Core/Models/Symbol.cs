namespace Ecad.Core.Models;

/// <summary>
/// A schematic symbol in the library. Deliberately minimal for M1 — connection-point and
/// variant (rotation/mirror) geometry tables are designed in M4 once the SVG symbol format
/// is defined; Placement only needs a stable SymbolId to reference in the meantime.
/// </summary>
public class Symbol
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LibraryName { get; set; }
    public string? SvgFilePath { get; set; }
    public string? Category { get; set; }
}
