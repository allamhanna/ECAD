using Ecad.Core.Enums;

namespace Ecad.Core.Models;

/// <summary>A ConnectionEnd joined with its parent Connection's WireNumber/CrossSectionMm2 and both
/// endpoint DevicePinIds — the M9 Terminations tab's read shape (Section 6.3's filterable view).
/// Endpoint labels are resolved by the caller from FromDevicePinId/ToDevicePinId, not baked into
/// this join — same "label building is app-facing formatting, not SQL" precedent as
/// ConnectionsGridViewModel.FormatDeviceTag.</summary>
public class ConnectionEndWithContext
{
    public long Id { get; set; }
    public long ConnectionId { get; set; }
    public ConnectionEndDesignator End { get; set; }
    public bool TerminationEnabled { get; set; }
    public TerminationType TerminationType { get; set; } = TerminationType.None;
    public long? TerminationPartId { get; set; }
    public double? StrippingLengthMm { get; set; }

    public string? WireNumber { get; set; }
    public double? CrossSectionMm2 { get; set; }
    public long FromDevicePinId { get; set; }
    public long ToDevicePinId { get; set; }
}
