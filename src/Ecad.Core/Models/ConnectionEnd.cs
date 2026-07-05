using Ecad.Core.Enums;

namespace Ecad.Core.Models;

/// <summary>One end (From or To) of a Connection, with its own independently toggleable termination.</summary>
public class ConnectionEnd
{
    public long Id { get; set; }
    public long ConnectionId { get; set; }
    public ConnectionEndDesignator End { get; set; }

    public bool TerminationEnabled { get; set; }
    public TerminationType TerminationType { get; set; } = TerminationType.None;
    public long? TerminationPartId { get; set; }
    public double? StrippingLengthMm { get; set; }
}
