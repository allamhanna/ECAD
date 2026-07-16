using Ecad.Core.Models;
using Ecad.Reports.Builders;
using Xunit;

namespace Ecad.Reports.Tests.Builders;

public class CableOverviewReportBuilderTests
{
    [Fact]
    public void Build_ProjectsTagTypeLengthCoreCountAndLocations()
    {
        var cable = new Cable { Id = 1, Tag = "-W1", TypeDesignation = "H07V-K", LengthMm = 1500 };
        var core1 = new CableCore { Id = 10, CableId = 1, CoreNumber = 1 };
        var core2 = new CableCore { Id = 11, CableId = 1, CoreNumber = 2 };
        var deviceA = new Device { Id = 1, LocationSegment = "L1" };
        var deviceB = new Device { Id = 2, LocationSegment = "L2" };
        var pinA = new DevicePin { Id = 100, DeviceId = 1, Name = "1" };
        var pinB = new DevicePin { Id = 101, DeviceId = 2, Name = "1" };
        var connection = new Connection { Id = 1000, FromDevicePinId = 100, ToDevicePinId = 101, CableId = 1 };

        var context = CableOverviewReportBuilder.Build([cable], [core1, core2], [connection], [pinA, pinB], [deviceA, deviceB]);

        var row = Assert.Single(context.GetTable("Cables"));
        Assert.Equal("-W1", row["Tag"]);
        Assert.Equal("H07V-K", row["TypeDesignation"]);
        Assert.Equal(1500.0, row["LengthMm"]);
        Assert.Equal(2, row["CoreCount"]);
        Assert.Equal("L1", row["FromLocation"]);
        Assert.Equal("L2", row["ToLocation"]);
    }

    [Fact]
    public void Build_CableWithNoConnections_ZeroCoreCountAndBlankLocations()
    {
        var cable = new Cable { Id = 1, Tag = "-W2" };

        var context = CableOverviewReportBuilder.Build([cable], [], [], [], []);

        var row = Assert.Single(context.GetTable("Cables"));
        Assert.Equal(0, row["CoreCount"]);
        Assert.Equal(string.Empty, row["FromLocation"]);
        Assert.Equal(string.Empty, row["ToLocation"]);
    }
}
