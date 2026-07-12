namespace Ecad.Core.Models;

/// <summary>
/// A cable definition line — a straight line drawn on a schematic page that crosses one or more wires,
/// assigning each to a core of the referenced Cable. Own absolute geometry, independent of any
/// Connection's own (recomputed-live, deletable/recreatable) route.
/// </summary>
public class CableLine
{
    public long Id { get; set; }
    public long PageId { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public long CableId { get; set; }
}
