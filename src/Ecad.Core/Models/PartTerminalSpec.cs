namespace Ecad.Core.Models;

/// <summary>
/// Physical terminal geometry and wire-gauge/torque limits for a part's terminal, sourced from
/// EPLAN's connectionpoints.xml. Feeds termination/ferrule matching (Section 6.3).
/// </summary>
public class PartTerminalSpec
{
    public long Id { get; set; }
    public long PartId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Pos { get; set; }
    public double? MinCrossSectionMm2 { get; set; }
    public double? MaxCrossSectionMm2 { get; set; }
    public double? MinTorqueNm { get; set; }
    public double? MaxTorqueNm { get; set; }
    public int? MaxWireCount { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}
