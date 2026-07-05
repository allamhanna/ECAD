namespace Ecad.Core.Models;

public class Cable
{
    public long Id { get; set; }
    public string Tag { get; set; } = string.Empty;
    public long? PartId { get; set; }
    public string? TypeDesignation { get; set; }
    public double? LengthMm { get; set; }

    /// <summary>e.g. FER-FER, FER-CONN, FER-COMP, CONN-CONN — selects the manufacturing sheet layout.</summary>
    public string? EndTypeClassification { get; set; }
}
