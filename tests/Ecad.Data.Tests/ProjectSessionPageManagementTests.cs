using Ecad.Core.Enums;
using Ecad.Core.Models;
using Xunit;

namespace Ecad.Data.Tests;

public class ProjectSessionPageManagementTests
{
    private static ProjectSession CreateSession(TempSqliteFile file) =>
        ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });

    [Fact]
    public void RenamePage_UpdatesSegmentsAndPageType()
    {
        using var file = new TempSqliteFile();
        using var session = CreateSession(file);
        var page = session.AddPage(new Page { PageNumberSegment = "1" });

        session.RenamePage(page.Id, "F1", "L1", "DT", "5", PageType.CableDrawing);

        var updated = session.Pages.Single(p => p.Id == page.Id);
        Assert.Equal("F1", updated.FunctionSegment);
        Assert.Equal("L1", updated.LocationSegment);
        Assert.Equal("DT", updated.DocumentTypeSegment);
        Assert.Equal("5", updated.PageNumberSegment);
        Assert.Equal(PageType.CableDrawing, updated.PageType);
    }

    [Fact]
    public void RenumberPages_AssignsSequentialNumbersInGivenOrder()
    {
        using var file = new TempSqliteFile();
        using var session = CreateSession(file);
        var pageA = session.AddPage(new Page { PageNumberSegment = "9" });
        var pageB = session.AddPage(new Page { PageNumberSegment = "1" });

        session.RenumberPages([pageB.Id, pageA.Id]);

        Assert.Equal("1", session.Pages.Single(p => p.Id == pageB.Id).PageNumberSegment);
        Assert.Equal("2", session.Pages.Single(p => p.Id == pageA.Id).PageNumberSegment);
    }

    [Fact]
    public void DeletePagesCascade_RemovesPageAndDeletesDeviceWithNoOtherPlacements()
    {
        using var file = new TempSqliteFile();
        using var session = CreateSession(file);
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var placement = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, null, null, "X1");

        session.DeletePagesCascade([page.Id]);

        Assert.DoesNotContain(session.Pages, p => p.Id == page.Id);
        Assert.Null(session.GetDevice(placement.DeviceId));
    }

    [Fact]
    public void DeletePagesCascade_DeviceStillPlacedElsewhere_DeviceSurvives()
    {
        using var file = new TempSqliteFile();
        using var session = CreateSession(file);
        var pageA = session.AddPage(new Page { PageNumberSegment = "1" });
        var pageB = session.AddPage(new Page { PageNumberSegment = "2" });
        var placementA = session.PlaceSymbol(pageA.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, null, null, "X1");
        session.PlaceSymbolOnExistingDevice(placementA.DeviceId, pageB.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["2"], 0, 0);

        session.DeletePagesCascade([pageA.Id]);

        Assert.DoesNotContain(session.Pages, p => p.Id == pageA.Id);
        Assert.NotNull(session.GetDevice(placementA.DeviceId));
        Assert.Single(session.GetPlacements(pageB.Id));
    }

    [Fact]
    public void DeletePagesCascade_RemovesDependentConnections()
    {
        using var file = new TempSqliteFile();
        using var session = CreateSession(file);
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var a = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, null, null, "X1");
        var b = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, null, null, "X2");
        session.CreateConnection(session.GetDevicePins(a.DeviceId).Single().Id, session.GetDevicePins(b.DeviceId).Single().Id);

        session.DeletePagesCascade([page.Id]);

        Assert.Empty(session.GetAllConnections());
    }

    [Fact]
    public void DeletePagesCascade_ClosedOverMultiplePages_RemovesAll()
    {
        using var file = new TempSqliteFile();
        using var session = CreateSession(file);
        var pageA = session.AddPage(new Page { PageNumberSegment = "1" });
        var pageB = session.AddPage(new Page { PageNumberSegment = "2" });

        session.DeletePagesCascade([pageA.Id, pageB.Id]);

        Assert.Empty(session.Pages);
    }

    [Fact]
    public void UpdatePageNavigatorSettings_PersistsOnCurrentProjectAndReloadsFromDisk()
    {
        using var file = new TempSqliteFile();
        using (var session = CreateSession(file))
        {
            session.UpdatePageNavigatorSettings("""{"GroupBy":"Location"}""");
            Assert.Equal("""{"GroupBy":"Location"}""", session.CurrentProject.PageNavigatorSettingsJson);
        }

        using var reopened = ProjectSession.Open(file.Path);
        Assert.Equal("""{"GroupBy":"Location"}""", reopened.CurrentProject.PageNavigatorSettingsJson);
    }
}
