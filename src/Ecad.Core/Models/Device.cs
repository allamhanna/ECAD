namespace Ecad.Core.Models;

public class Device
{
    public long Id { get; set; }
    public long ProjectId { get; set; }

    public string? FunctionSegment { get; set; }
    public string? LocationSegment { get; set; }
    public string DeviceTagSegment { get; set; } = string.Empty;

    /// <summary>References the project-local cached Part, if this device is assigned one.</summary>
    public long? PartId { get; set; }
}
