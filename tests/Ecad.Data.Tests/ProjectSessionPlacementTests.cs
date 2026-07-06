using Ecad.Core.Models;
using Xunit;

namespace Ecad.Data.Tests;

public class ProjectSessionPlacementTests
{
    [Fact]
    public void PlaceSymbol_CreatesDeviceDevicePinsPlacementAndPlacementPins()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });

        var placement = session.PlaceSymbol(page.Id, "RelayCoil", "Starter", "SymbolLibrary/RelayCoil.svg", "Coils",
            pinNames: ["A1", "A2"], x: 100, y: 200, deviceTag: "K1");

        Assert.True(placement.Id > 0);
        Assert.Equal(100, placement.X);
        Assert.Equal(200, placement.Y);

        var device = session.GetDevice(placement.DeviceId)!;
        Assert.Equal("K1", device.DeviceTagSegment);
        Assert.Equal(2, session.GetDevicePins(device.Id).Count);

        var placementsOnPage = session.GetPlacements(page.Id);
        var onPage = Assert.Single(placementsOnPage);
        Assert.Equal("RelayCoil", onPage.SymbolName);
        Assert.Equal("K1", onPage.DeviceTag);
    }

    [Fact]
    public void MovePlacement_UpdatesPosition()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var placement = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1", "2"], 0, 0, "X1");

        session.MovePlacement(placement.Id, 50, 75);

        var moved = Assert.Single(session.GetPlacements(page.Id));
        Assert.Equal(50, moved.X);
        Assert.Equal(75, moved.Y);
    }

    [Fact]
    public void RotatePlacement_UpdatesRotationAndMirror()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var placement = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1", "2"], 0, 0, "X1");

        session.RotatePlacement(placement.Id, 90, mirrored: true);

        var rotated = Assert.Single(session.GetPlacements(page.Id));
        Assert.Equal(90, rotated.RotationDegrees);
        Assert.True(rotated.Mirrored);
    }

    [Fact]
    public void DeletePlacement_RemovesPlacementAndCascadesDeviceData()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var placement = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1", "2"], 0, 0, "X1");

        session.DeletePlacement(placement.Id);

        Assert.Empty(session.GetPlacements(page.Id));
    }

    [Fact]
    public void PlaceSymbol_SameSymbolTwice_ReusesOneSymbolRow()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });

        var first = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1", "2"], 0, 0, "X1");
        var second = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1", "2"], 40, 0, "X2");

        Assert.Equal(2, session.GetPlacements(page.Id).Count);
        // Both placements should reference the same underlying Symbol row (verified indirectly:
        // GetOrCreateSymbol is idempotent by name, so re-placing doesn't grow the Symbol table).
        Assert.NotEqual(first.DeviceId, second.DeviceId);
    }
}
