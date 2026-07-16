using Ecad.Reports.LayoutSchema;
using Xunit;

namespace Ecad.Reports.Tests.LayoutSchema;

public class ReportLayoutLoaderTests
{
    [Fact]
    public void LoadFromFolder_MissingFolder_ReturnsEmptyWithWarning_NoException()
    {
        var result = ReportLayoutLoader.LoadFromFolder(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        Assert.Empty(result.Layouts);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void LoadFromFolder_ValidJsonFile_ParsesReportKindAndTableRegion()
    {
        using var folder = new TempFolder();
        File.WriteAllText(Path.Combine(folder.Path, "ConnectionList.json"), """
        {
            "ReportKind": "ConnectionList",
            "Variant": null,
            "Page": { "PaperSize": "A3", "Orientation": "Landscape", "MarginLeftMm": 10, "MarginTopMm": 10, "MarginRightMm": 10, "MarginBottomMm": 10 },
            "Header": null,
            "Footer": null,
            "Body": [
                { "kind": "Table", "DataSourceKey": "Connections", "Columns": [ { "Header": "Wire", "DataFieldKey": "WireNumber", "WidthMm": 20, "Align": "Left" } ] }
            ],
            "PageBreak": { "MaxRowsPerPage": null, "OneEntityPerPage": false }
        }
        """);

        var result = ReportLayoutLoader.LoadFromFolder(folder.Path);

        Assert.Empty(result.Warnings);
        var layout = Assert.Single(result.Layouts);
        Assert.Equal("ConnectionList", layout.ReportKind);
        var table = Assert.IsType<RepeatingTableRegion>(Assert.Single(layout.Body));
        Assert.Equal("Connections", table.DataSourceKey);
        Assert.Single(table.Columns);
    }

    [Fact]
    public void LoadFromFolder_MalformedJsonFile_ProducesWarning_DoesNotThrow_SkipsOnlyThatFile()
    {
        using var folder = new TempFolder();
        File.WriteAllText(Path.Combine(folder.Path, "Broken.json"), "{ not valid json");
        File.WriteAllText(Path.Combine(folder.Path, "Bom.json"), """
        {
            "ReportKind": "Bom",
            "Variant": null,
            "Page": { "PaperSize": "A4", "Orientation": "Portrait", "MarginLeftMm": 10, "MarginTopMm": 10, "MarginRightMm": 10, "MarginBottomMm": 10 },
            "Header": null,
            "Footer": null,
            "Body": [],
            "PageBreak": { "MaxRowsPerPage": null, "OneEntityPerPage": false }
        }
        """);

        var result = ReportLayoutLoader.LoadFromFolder(folder.Path);

        Assert.Single(result.Warnings);
        var layout = Assert.Single(result.Layouts);
        Assert.Equal("Bom", layout.ReportKind);
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());

        public TempFolder() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
