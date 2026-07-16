using Ecad.Core.Enums;
using Ecad.Core.Models;
using Ecad.Reports.Builders;
using Ecad.Reports.LayoutSchema;
using Xunit;

namespace Ecad.Reports.Tests.Builders;

public class CableManufacturingSheetReportBuilderTests
{
    private static ReportLayout MakeLayout(string? variant) => new(
        ReportKind: CableManufacturingSheetReportBuilder.ReportKind,
        Variant: variant,
        Page: new PageSetup("A4", "Portrait", 10, 10, 10, 10),
        Header: null,
        Footer: null,
        Body: [],
        PageBreak: new PageBreakRule(null, true));

    [Fact]
    public void Build_SelectsLayoutMatchingEndTypeClassification()
    {
        var ferFerLayout = MakeLayout("FER-FER");
        var ferConnLayout = MakeLayout("FER-CONN");
        var cable = new Cable { Id = 1, Tag = "-W1", EndTypeClassification = "FER-CONN" };

        var result = CableManufacturingSheetReportBuilder.Build(cable, [], [], [], [], [], [ferFerLayout, ferConnLayout]);

        Assert.Same(ferConnLayout, result.Layout);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void Build_NoMatchingVariant_FallsBackToDefaultLayout_WithWarning()
    {
        var defaultLayout = MakeLayout(null);
        var cable = new Cable { Id = 1, Tag = "-W1", EndTypeClassification = "FER-COMP" };

        var result = CableManufacturingSheetReportBuilder.Build(cable, [], [], [], [], [], [defaultLayout]);

        Assert.Same(defaultLayout, result.Layout);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void Build_NoLayoutAtAll_Throws()
    {
        var cable = new Cable { Id = 1, Tag = "-W1" };

        Assert.Throws<InvalidOperationException>(() =>
            CableManufacturingSheetReportBuilder.Build(cable, [], [], [], [], [], []));
    }

    [Fact]
    public void Build_PerCoreRows_IncludeConnectionAndTerminationData()
    {
        var layout = MakeLayout(null);
        var cable = new Cable { Id = 1, Tag = "-W1" };
        var core = new CableCore { Id = 10, CableId = 1, CoreNumber = 1, Color = "BU" };
        var deviceA = new Device { Id = 1, DeviceTagSegment = "X1" };
        var deviceB = new Device { Id = 2, DeviceTagSegment = "X2" };
        var pinA = new DevicePin { Id = 100, DeviceId = 1, Name = "1" };
        var pinB = new DevicePin { Id = 101, DeviceId = 2, Name = "1" };
        var connection = new Connection { Id = 1000, FromDevicePinId = 100, ToDevicePinId = 101, CableId = 1, CableCoreId = 10 };
        var fromEnd = new ConnectionEndWithContext { ConnectionId = 1000, End = ConnectionEndDesignator.From, TerminationEnabled = true, StrippingLengthMm = 8 };

        var result = CableManufacturingSheetReportBuilder.Build(
            cable, [core], [connection], [fromEnd], [pinA, pinB], [deviceA, deviceB], [layout]);

        var row = Assert.Single(result.Data.GetTable("Cores"));
        Assert.Equal(1, row["CoreNumber"]);
        Assert.Equal("BU", row["Color"]);
        Assert.Equal("-X1:1", row["FromPin"]);
        Assert.Equal("-X2:1", row["ToPin"]);
        Assert.Equal(8.0, row["FromStrippingMm"]);
    }
}
