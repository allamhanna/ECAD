using Ecad.Core.Enums;
using Ecad.Core.Models;
using Ecad.Reports.Builders;
using Xunit;

namespace Ecad.Reports.Tests.Builders;

public class ConnectionListReportBuilderTests
{
    [Fact]
    public void Build_ResolvesFromToTagsAndWireData()
    {
        var deviceA = new Device { Id = 1, DeviceTagSegment = "X1", LocationSegment = "L1" };
        var deviceB = new Device { Id = 2, DeviceTagSegment = "X2" };
        var pinA = new DevicePin { Id = 10, DeviceId = 1, Name = "1" };
        var pinB = new DevicePin { Id = 20, DeviceId = 2, Name = "2" };
        var connection = new Connection { Id = 100, FromDevicePinId = 10, ToDevicePinId = 20, WireNumber = "W1", Color = "RD", CrossSectionMm2 = 1.5 };

        var context = ConnectionListReportBuilder.Build(
            [connection], [], [deviceA, deviceB], [pinA, pinB], [], [], []);

        var row = Assert.Single(context.GetTable("Connections"));
        Assert.Equal("+L1 -X1:1", row["FromTag"]);
        Assert.Equal("-X2:2", row["ToTag"]);
        Assert.Equal("W1", row["WireNumber"]);
        Assert.Equal("RD", row["Color"]);
        Assert.Equal(1.5, row["CrossSectionMm2"]);
        Assert.Equal(string.Empty, row["CableTag"]);
        Assert.Equal(string.Empty, row["CoreNumber"]);
    }

    [Fact]
    public void Build_TerminationEnabled_ShowsPartTypeNumber_DisabledShowsBlank()
    {
        var device = new Device { Id = 1, DeviceTagSegment = "X1" };
        var pinA = new DevicePin { Id = 10, DeviceId = 1, Name = "1" };
        var pinB = new DevicePin { Id = 11, DeviceId = 1, Name = "2" };
        var connection = new Connection { Id = 100, FromDevicePinId = 10, ToDevicePinId = 11 };
        var part = new Part { Id = 5, ExternalKey = "EK1", TypeNumber = "FERRULE-1.5" };
        var fromEnd = new ConnectionEndWithContext { ConnectionId = 100, End = ConnectionEndDesignator.From, TerminationEnabled = true, TerminationPartId = 5 };
        var toEnd = new ConnectionEndWithContext { ConnectionId = 100, End = ConnectionEndDesignator.To, TerminationEnabled = false };

        var context = ConnectionListReportBuilder.Build(
            [connection], [fromEnd, toEnd], [device], [pinA, pinB], [part], [], []);

        var row = Assert.Single(context.GetTable("Connections"));
        Assert.Equal("FERRULE-1.5", row["FromTermination"]);
        Assert.Equal(string.Empty, row["ToTermination"]);
    }

    [Fact]
    public void Build_CableAssigned_ShowsCableTagAndCoreNumber()
    {
        var device = new Device { Id = 1, DeviceTagSegment = "X1" };
        var pinA = new DevicePin { Id = 10, DeviceId = 1, Name = "1" };
        var pinB = new DevicePin { Id = 11, DeviceId = 1, Name = "2" };
        var cable = new Cable { Id = 7, Tag = "-W1" };
        var core = new CableCore { Id = 70, CableId = 7, CoreNumber = 3 };
        var connection = new Connection { Id = 100, FromDevicePinId = 10, ToDevicePinId = 11, CableId = 7, CableCoreId = 70 };

        var context = ConnectionListReportBuilder.Build(
            [connection], [], [device], [pinA, pinB], [], [cable], [core]);

        var row = Assert.Single(context.GetTable("Connections"));
        Assert.Equal("-W1", row["CableTag"]);
        Assert.Equal("3", row["CoreNumber"]);
    }
}
