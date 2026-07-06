using Ecad.Core.Models;
using Xunit;

namespace Ecad.Data.Tests;

public class ProjectSessionMultiPlacementTests
{
    [Fact]
    public void PlaceSymbolOnExistingDevice_SharesTagAndAddsNewDevicePins()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var coilPage = session.AddPage(new Page { PageNumberSegment = "5" });
        var contactPage = session.AddPage(new Page { PageNumberSegment = "12" });

        var coil = session.PlaceSymbol(coilPage.Id, "RelayCoil", null, "SymbolLibrary/RelayCoil.svg", "Coils",
            ["A1", "A2"], 0, 0, "F1", "L1", "K1");
        var contact = session.PlaceSymbolOnExistingDevice(coil.DeviceId, contactPage.Id, "ContactNO", null,
            "SymbolLibrary/ContactNO.svg", "Contacts", ["13", "14"], 30, 0);

        Assert.Equal(coil.DeviceId, contact.DeviceId);
        var device = session.GetDevice(coil.DeviceId)!;
        Assert.Equal("K1", device.DeviceTagSegment);

        var pinNames = session.GetDevicePins(coil.DeviceId).Select(p => p.Name).ToList();
        Assert.Equal(new[] { "A1", "A2", "13", "14" }, pinNames);
    }

    [Fact]
    public void DeletePlacement_NonLastPlacement_KeepsDeviceAndSiblingAlive_RemovesOnlyExclusivePins()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var coilPage = session.AddPage(new Page { PageNumberSegment = "5" });
        var contactPage = session.AddPage(new Page { PageNumberSegment = "12" });

        var coil = session.PlaceSymbol(coilPage.Id, "RelayCoil", null, "SymbolLibrary/RelayCoil.svg", "Coils",
            ["A1", "A2"], 0, 0, null, null, "K1");
        var contact = session.PlaceSymbolOnExistingDevice(coil.DeviceId, contactPage.Id, "ContactNO", null,
            "SymbolLibrary/ContactNO.svg", "Contacts", ["13", "14"], 30, 0);

        var result = session.DeletePlacement(contact.Id);

        Assert.False(result.DeviceDeleted);
        Assert.NotNull(session.GetDevice(coil.DeviceId));
        Assert.Single(session.GetPlacements(coilPage.Id));

        var remainingPinNames = session.GetDevicePins(coil.DeviceId).Select(p => p.Name).ToList();
        Assert.Equal(new[] { "A1", "A2" }, remainingPinNames);
    }

    [Fact]
    public void DeletePlacement_LastPlacement_RemovesWholeDevice()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var placement = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals",
            ["1", "2"], 0, 0, null, null, "X1");

        var result = session.DeletePlacement(placement.Id);

        Assert.True(result.DeviceDeleted);
        Assert.Null(session.GetDevice(placement.DeviceId));
        Assert.Empty(session.GetPlacements(page.Id));
    }

    [Fact]
    public void IsTagAvailable_FalseWhenUsed_TrueWhenExcludingSelf_TrueWhenUnused()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var placement = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals",
            ["1", "2"], 0, 0, "F1", "L1", "K1");

        Assert.False(session.IsTagAvailable("F1", "L1", "K1", excludingDeviceId: null));
        Assert.True(session.IsTagAvailable("F1", "L1", "K1", excludingDeviceId: placement.DeviceId));
        Assert.True(session.IsTagAvailable("F1", "L1", "K2", excludingDeviceId: null));
        Assert.True(session.IsTagAvailable(null, null, "K1", excludingDeviceId: null));
    }

    [Fact]
    public void SuggestNextDesignation_IncrementsWithinScope_AndIsScopedByFunctionAndLocation()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });

        Assert.Equal("1", session.SuggestNextDesignation("F1", "L1"));

        session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, "F1", "L1", "K1");
        Assert.Equal("2", session.SuggestNextDesignation("F1", "L1"));

        session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, "F1", "L1", "K2");
        Assert.Equal("3", session.SuggestNextDesignation("F1", "L1"));

        // A different Function+Location scope isn't affected by the above.
        Assert.Equal("1", session.SuggestNextDesignation("F2", "L1"));
    }

    [Fact]
    public void GetPlacements_ReturnsSiblingLabels_ForMultiPlacementDevice_AndEmptyForSingle()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var coilPage = session.AddPage(new Page { PageNumberSegment = "5" });
        var contactPage = session.AddPage(new Page { PageNumberSegment = "12" });

        var coil = session.PlaceSymbol(coilPage.Id, "RelayCoil", null, "SymbolLibrary/RelayCoil.svg", "Coils",
            ["A1", "A2"], 0, 0, null, null, "K1");
        session.PlaceSymbolOnExistingDevice(coil.DeviceId, contactPage.Id, "ContactNO", null,
            "SymbolLibrary/ContactNO.svg", "Contacts", ["13", "14"], 0, 0);
        var lonely = session.PlaceSymbol(coilPage.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals",
            ["1", "2"], 40, 0, null, null, "X1");

        var coilOnPage = session.GetPlacements(coilPage.Id).Single(p => p.PlacementId == coil.Id);
        var coilSibling = Assert.Single(coilOnPage.Siblings);
        Assert.Equal("12", coilSibling.PageLabel);

        var lonelyOnPage = session.GetPlacements(coilPage.Id).Single(p => p.PlacementId == lonely.Id);
        Assert.Empty(lonelyOnPage.Siblings);
    }
}
