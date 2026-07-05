using Ecad.Core.Enums;

namespace Ecad.Core.Models;

/// <summary>A user-defined property definition, attachable to Parts/Devices/Connections/Cables.</summary>
public class UdpDefinition
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public UdpDataType DataType { get; set; }
    public string? Unit { get; set; }

    /// <summary>JSON array of allowed values when DataType is Enum; null otherwise.</summary>
    public string? EnumValuesJson { get; set; }

    public UdpEntityType AppliesToEntityType { get; set; }
}
