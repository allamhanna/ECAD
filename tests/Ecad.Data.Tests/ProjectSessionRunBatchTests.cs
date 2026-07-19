using Ecad.Core.Models;
using Xunit;

namespace Ecad.Data.Tests;

public class ProjectSessionRunBatchTests
{
    [Fact]
    public void RunBatch_CommitsAllWritesTogether()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });

        session.RunBatch(() =>
        {
            session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, "F1", "L1", "K1");
            session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, "F1", "L1", "K2");
            session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 80, 0, "F1", "L1", "K3");
        });

        Assert.Equal(3, session.GetPlacements(page.Id).Count);
    }

    [Fact]
    public void RunBatch_ThrowingPartway_RollsBackEverything()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });

        Assert.Throws<InvalidOperationException>(() => session.RunBatch(() =>
        {
            session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, "F1", "L1", "K1");
            throw new InvalidOperationException("simulated failure partway through the batch");
        }));

        Assert.Empty(session.GetPlacements(page.Id));
    }

    [Fact]
    public void RunBatch_FiresPlacementsChangedOnceForTheWholeBatchNotOncePerWrite()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });

        var raiseCount = 0;
        session.PlacementsChanged += () => raiseCount++;

        session.RunBatch(() =>
        {
            session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, "F1", "L1", "K1");
            session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, "F1", "L1", "K2");
        });

        Assert.Equal(1, raiseCount);
    }

    [Fact]
    public void DeletePlacement_OutsideBatch_StillFiresImmediatelyAsBefore()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var placement = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, "F1", "L1", "K1");

        var raiseCount = 0;
        session.PlacementsChanged += () => raiseCount++;

        session.DeletePlacement(placement.Id);

        Assert.Equal(1, raiseCount);
        Assert.Empty(session.GetPlacements(page.Id));
    }
}
