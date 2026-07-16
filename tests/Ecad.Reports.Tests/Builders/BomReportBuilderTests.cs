using Ecad.Core.Enums;
using Ecad.Core.Models;
using Ecad.Reports.Builders;
using Xunit;

namespace Ecad.Reports.Tests.Builders;

public class BomReportBuilderTests
{
    [Fact]
    public void Build_PerProject_AggregatesIdenticalPartAcrossDevices()
    {
        var part = new Part { Id = 1, ExternalKey = "EK1" };
        var deviceA = new Device { Id = 1, PartId = 1 };
        var deviceB = new Device { Id = 2, PartId = 1 };

        var context = BomReportBuilder.Build([deviceA, deviceB], [], [], [], [], [part], BomGroupingMode.PerProject);

        var row = Assert.Single(context.GetTable("BomLines"));
        Assert.Equal(2, row["Quantity"]);
        Assert.Equal(string.Empty, row["Module"]);
    }

    [Fact]
    public void Build_PerLocation_SplitsByDeviceLocationSegment()
    {
        var part = new Part { Id = 1, ExternalKey = "EK1" };
        var deviceA = new Device { Id = 1, PartId = 1, LocationSegment = "L1" };
        var deviceB = new Device { Id = 2, PartId = 1, LocationSegment = "L2" };

        var context = BomReportBuilder.Build([deviceA, deviceB], [], [], [], [], [part], BomGroupingMode.PerLocation);

        var rows = context.GetTable("BomLines");
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => (string)r["Location"]! == "L1" && (int)r["Quantity"]! == 1);
        Assert.Contains(rows, r => (string)r["Location"]! == "L2" && (int)r["Quantity"]! == 1);
    }

    [Fact]
    public void Build_PerCableAssembly_CablePartAndTerminationsCountedOnceInModule_NotInFlatTotal()
    {
        var cablePart = new Part { Id = 1, ExternalKey = "CABLE-PART" };
        var ferrulePart = new Part { Id = 2, ExternalKey = "FERRULE" };
        var looseDevicePart = new Part { Id = 3, ExternalKey = "LOOSE-DEVICE" };

        var cable = new Cable { Id = 1, Tag = "-W1", PartId = 1 };
        var wiredDevice = new Device { Id = 1 };
        var pin = new DevicePin { Id = 10, DeviceId = 1, Name = "1" };
        var connection = new Connection { Id = 100, FromDevicePinId = 10, ToDevicePinId = 10, CableId = 1 };
        var terminationEnd = new ConnectionEndWithContext
        {
            ConnectionId = 100,
            End = ConnectionEndDesignator.From,
            TerminationEnabled = true,
            TerminationPartId = 2,
            FromDevicePinId = 10,
            ToDevicePinId = 10,
        };
        var looseDevice = new Device { Id = 2, PartId = 3 };

        var context = BomReportBuilder.Build(
            [wiredDevice, looseDevice], [pin], [connection], [terminationEnd], [cable], [cablePart, ferrulePart, looseDevicePart],
            BomGroupingMode.PerCableAssembly);

        var rows = context.GetTable("BomLines");

        var flatRows = rows.Where(r => (string)r["Module"]! == string.Empty).ToList();
        var flatPartKeys = flatRows.Select(r => (string)r["ExternalKey"]!).ToList();
        Assert.Contains("LOOSE-DEVICE", flatPartKeys);
        Assert.DoesNotContain("CABLE-PART", flatPartKeys);
        Assert.DoesNotContain("FERRULE", flatPartKeys);

        var moduleRows = rows.Where(r => (string)r["Module"]! == "-W1").ToList();
        Assert.Contains(moduleRows, r => (string)r["ExternalKey"]! == "CABLE-PART" && (int)r["Quantity"]! == 1);
        Assert.Contains(moduleRows, r => (string)r["ExternalKey"]! == "FERRULE" && (int)r["Quantity"]! == 1);
    }
}
