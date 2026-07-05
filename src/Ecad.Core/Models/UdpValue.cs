using Ecad.Core.Enums;

namespace Ecad.Core.Models;

/// <summary>An EAV-style value of a UdpDefinition attached to a specific entity instance.</summary>
public class UdpValue
{
    public long Id { get; set; }
    public long DefinitionId { get; set; }
    public UdpEntityType EntityType { get; set; }
    public long EntityId { get; set; }
    public string? Value { get; set; }
}
