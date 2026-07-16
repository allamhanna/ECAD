using Ecad.Core.Enums;
using Ecad.Core.Models;
using Xunit;

namespace Ecad.Data.Tests;

public class ProjectSessionGeneratedReportTests
{
    private static ProjectSession CreateSession(TempSqliteFile file) =>
        ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });

    [Fact]
    public void UpsertGeneratedReportPage_NewIdentity_CreatesReportTypePageUnderDocumentTypeSegment()
    {
        using var file = new TempSqliteFile();
        using var session = CreateSession(file);

        var result = session.UpsertGeneratedReportPage("ConnectionList", "RPT-CONN", null, null);

        Assert.True(result.WasNewlyCreated);
        Assert.Equal(PageType.Report, result.Page.PageType);
        Assert.Equal("RPT-CONN", result.Page.DocumentTypeSegment);
        Assert.Equal("1", result.Page.PageNumberSegment);
        Assert.Contains(session.Pages, p => p.Id == result.Page.Id);
    }

    [Fact]
    public void UpsertGeneratedReportPage_SameIdentity_ReusesExistingPage_NoDuplicate()
    {
        using var file = new TempSqliteFile();
        using var session = CreateSession(file);

        var first = session.UpsertGeneratedReportPage("ConnectionList", "RPT-CONN", null, null);
        var second = session.UpsertGeneratedReportPage("ConnectionList", "RPT-CONN", null, null);

        Assert.False(second.WasNewlyCreated);
        Assert.Equal(first.Page.Id, second.Page.Id);
        Assert.Single(session.Pages, p => p.DocumentTypeSegment == "RPT-CONN");
    }

    [Fact]
    public void UpsertGeneratedReportPage_DifferentGroupingKey_CreatesSeparatePage()
    {
        using var file = new TempSqliteFile();
        using var session = CreateSession(file);

        var project = session.UpsertGeneratedReportPage("Bom", "RPT-BOM", null, "Project");
        var location = session.UpsertGeneratedReportPage("Bom", "RPT-BOM", null, "Location");

        Assert.NotEqual(project.Page.Id, location.Page.Id);
        Assert.Equal(2, session.Pages.Count(p => p.DocumentTypeSegment == "RPT-BOM"));
    }

    [Fact]
    public void UpsertGeneratedReportPage_DifferentSourceEntityId_CreatesSeparatePage()
    {
        using var file = new TempSqliteFile();
        using var session = CreateSession(file);
        var cableA = session.CreateCable(new Cable { Tag = "-W1" });
        var cableB = session.CreateCable(new Cable { Tag = "-W2" });

        var sheetA = session.UpsertGeneratedReportPage("CableManufacturingSheet", "RPT-F09", cableA.Id, null);
        var sheetB = session.UpsertGeneratedReportPage("CableManufacturingSheet", "RPT-F09", cableB.Id, null);

        Assert.NotEqual(sheetA.Page.Id, sheetB.Page.Id);
    }

    [Fact]
    public void DeleteCable_WithManufacturingSheetPage_RemovesThatPage()
    {
        using var file = new TempSqliteFile();
        using var session = CreateSession(file);
        var cable = session.CreateCable(new Cable { Tag = "-W1" });
        var sheet = session.UpsertGeneratedReportPage("CableManufacturingSheet", "RPT-F09", cable.Id, null);

        session.DeleteCable(cable.Id);

        Assert.DoesNotContain(session.Pages, p => p.Id == sheet.Page.Id);
        Assert.Null(session.GetGeneratedReportForPage(sheet.Page.Id));
    }

    [Fact]
    public void GetGeneratedReportForPage_ReturnsIdentityMatchingWhatWasUpserted()
    {
        using var file = new TempSqliteFile();
        using var session = CreateSession(file);
        var cable = session.CreateCable(new Cable { Tag = "-W1" });
        var sheet = session.UpsertGeneratedReportPage("CableManufacturingSheet", "RPT-F09", cable.Id, null);

        var report = session.GetGeneratedReportForPage(sheet.Page.Id);

        Assert.NotNull(report);
        Assert.Equal("CableManufacturingSheet", report!.ReportKind);
        Assert.Equal(cable.Id, report.SourceEntityId);
    }

    [Fact]
    public void DeleteOrphanedCableManufacturingSheets_RemovesPagesForCablesNoLongerLive_LeavesOthers()
    {
        using var file = new TempSqliteFile();
        using var session = CreateSession(file);
        var cableA = session.CreateCable(new Cable { Tag = "-W1" });
        var cableB = session.CreateCable(new Cable { Tag = "-W2" });
        var sheetA = session.UpsertGeneratedReportPage("CableManufacturingSheet", "RPT-F09", cableA.Id, null);
        var sheetB = session.UpsertGeneratedReportPage("CableManufacturingSheet", "RPT-F09", cableB.Id, null);

        session.DeleteOrphanedCableManufacturingSheets([cableB.Id]);

        Assert.DoesNotContain(session.Pages, p => p.Id == sheetA.Page.Id);
        Assert.Contains(session.Pages, p => p.Id == sheetB.Page.Id);
    }

    [Fact]
    public void DeletePage_RemovesItFromPagesAndRaisesPagesChanged()
    {
        using var file = new TempSqliteFile();
        using var session = CreateSession(file);
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var raised = false;
        session.PagesChanged += () => raised = true;

        session.DeletePage(page.Id);

        Assert.DoesNotContain(session.Pages, p => p.Id == page.Id);
        Assert.True(raised);
    }
}
