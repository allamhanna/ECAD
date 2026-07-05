namespace Ecad.Core.Models;

public class Connection
{
    public long Id { get; set; }
    public long FromDevicePinId { get; set; }
    public long ToDevicePinId { get; set; }

    public string? WireNumber { get; set; }
    public string? Color { get; set; }
    public double? CrossSectionMm2 { get; set; }
    public double? LengthMm { get; set; }

    /// <summary>The wire/conductor article, if any.</summary>
    public long? PartId { get; set; }

    public long? CableId { get; set; }
    public long? CableCoreId { get; set; }
}
