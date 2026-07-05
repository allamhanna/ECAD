namespace Ecad.Core.Models;

public class CableCore
{
    public long Id { get; set; }
    public long CableId { get; set; }
    public int CoreNumber { get; set; }
    public string? Color { get; set; }
    public double? CrossSectionMm2 { get; set; }
}
