namespace Ecad.Core.Models;

/// <summary>
/// A pin/connection template from the part's EPLAN function template data. When a Device is
/// assigned this Part, its templates seed the Device's DevicePin rows.
/// </summary>
public class PartPinTemplate
{
    public long Id { get; set; }
    public long PartId { get; set; }
    public int Pos { get; set; }
    public string? ConnectionDesignation { get; set; }
    public int? FunctionDefCategory { get; set; }
    public int? FunctionDefGroup { get; set; }
    public int? FunctionDefId { get; set; }
    public string? SymbolRef { get; set; }
}
