namespace Ecad.Core.Models;

/// <summary>A manufacturer or supplier. Both roles share this table; a Part references it via ManufacturerId/SupplierId.</summary>
public class Organization
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ExternalKey { get; set; }
}
