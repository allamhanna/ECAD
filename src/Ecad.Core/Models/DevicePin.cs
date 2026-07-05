namespace Ecad.Core.Models;

/// <summary>A logical pin of a Device, seeded from PartPinTemplate rows on Part assignment or entered manually.</summary>
public class DevicePin
{
    public long Id { get; set; }
    public long DeviceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Function { get; set; }
    public string? TechnicalData { get; set; }
}
