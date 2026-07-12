using Ecad.Core.Models;
using Xunit;

namespace Ecad.Data.Tests;

public class ProjectSessionCableLineTests
{
    private static (ProjectSession Session, Page Page, long Conn1, long Conn2) SetUpTwoConnections(TempSqliteFile file)
    {
        var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var a = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, null, null, "X1");
        var b = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, null, null, "X2");
        var c = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 80, 0, null, null, "X3");
        var d = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 120, 0, null, null, "X4");
        var conn1 = session.CreateConnection(session.GetDevicePins(a.DeviceId).Single().Id, session.GetDevicePins(b.DeviceId).Single().Id);
        var conn2 = session.CreateConnection(session.GetDevicePins(c.DeviceId).Single().Id, session.GetDevicePins(d.DeviceId).Single().Id);
        return (session, page, conn1.Id, conn2.Id);
    }

    [Fact]
    public void DrawCableLine_AcrossTwoConnections_CreatesSequentialCores_AndMirrorsBoth()
    {
        using var file = new TempSqliteFile();
        var (session, page, conn1, conn2) = SetUpTwoConnections(file);
        using var s = session;

        var result = s.DrawCableLine(page.Id, 10, -10, 10, 10, "-W1", [conn1, conn2]);

        Assert.Equal(2, result.AssignedConnectionIds.Count);
        Assert.Empty(result.SkippedConnectionIds);

        var conn1Updated = s.GetConnectionsForPage(page.Id).Single(c => c.Id == conn1);
        var conn2Updated = s.GetConnectionsForPage(page.Id).Single(c => c.Id == conn2);
        Assert.NotNull(conn1Updated.CableId);
        Assert.Equal(conn1Updated.CableId, conn2Updated.CableId);
        Assert.NotNull(conn1Updated.CableCoreId);
        Assert.NotNull(conn2Updated.CableCoreId);
        Assert.NotEqual(conn1Updated.CableCoreId, conn2Updated.CableCoreId);

        var cable = s.GetAllCables().Single();
        Assert.Equal("-W1", cable.Tag);
        var cores = s.GetCableCores(cable.Id).OrderBy(c => c.CoreNumber).ToList();
        Assert.Equal(2, cores.Count);
        Assert.Equal([1, 2], cores.Select(c => c.CoreNumber));
    }

    [Fact]
    public void DrawCableLine_ReusesExistingCableByTag_CaseInsensitive()
    {
        using var file = new TempSqliteFile();
        var (session, page, conn1, conn2) = SetUpTwoConnections(file);
        using var s = session;
        s.DrawCableLine(page.Id, 10, -10, 10, 10, "-W1", [conn1]);

        s.DrawCableLine(page.Id, 90, -10, 90, 10, "-w1", [conn2]);

        Assert.Single(s.GetAllCables());
    }

    [Fact]
    public void DrawCableLine_ConnectionAlreadyAssignedToDifferentCable_IsSkipped()
    {
        using var file = new TempSqliteFile();
        var (session, page, conn1, conn2) = SetUpTwoConnections(file);
        using var s = session;
        s.DrawCableLine(page.Id, 10, -10, 10, 10, "-W1", [conn1]);

        var result = s.DrawCableLine(page.Id, 50, -10, 50, 10, "-W2", [conn1, conn2]);

        Assert.Contains(conn1, result.SkippedConnectionIds);
        Assert.Contains(conn2, result.AssignedConnectionIds);
        var conn1Updated = s.GetConnectionsForPage(page.Id).Single(c => c.Id == conn1);
        Assert.Equal(s.GetAllCables().Single(c => c.Tag == "-W1").Id, conn1Updated.CableId);
    }

    [Fact]
    public void MoveCableLine_ReDetectingTheSameConnection_IsIdempotent_NoDuplicateCore()
    {
        using var file = new TempSqliteFile();
        var (session, page, conn1, _) = SetUpTwoConnections(file);
        using var s = session;
        var result = s.DrawCableLine(page.Id, 10, -10, 10, 10, "-W1", [conn1]);

        s.MoveCableLine(result.CableLineId, 12, -10, 12, 10, "-W1", [conn1]);

        var cable = s.GetAllCables().Single();
        Assert.Single(s.GetCableCores(cable.Id));
    }

    [Fact]
    public void DeleteCableLine_ClearsMirroredAssignments()
    {
        using var file = new TempSqliteFile();
        var (session, page, conn1, conn2) = SetUpTwoConnections(file);
        using var s = session;
        var result = s.DrawCableLine(page.Id, 10, -10, 10, 10, "-W1", [conn1, conn2]);

        s.DeleteCableLine(result.CableLineId);

        var conn1Updated = s.GetConnectionsForPage(page.Id).Single(c => c.Id == conn1);
        var conn2Updated = s.GetConnectionsForPage(page.Id).Single(c => c.Id == conn2);
        Assert.Null(conn1Updated.CableId);
        Assert.Null(conn1Updated.CableCoreId);
        Assert.Null(conn2Updated.CableId);
        Assert.Null(conn2Updated.CableCoreId);
        Assert.Empty(s.GetCableLineCrossings(result.CableLineId));
    }

    [Fact]
    public void DeleteConnection_WithLiveCableLineCrossing_OrphansTheCrossing_LineAndOtherCrossingsSurvive()
    {
        // The regression this entity design exists to fix: deleting a Connection (e.g. via auto-connect
        // rewiring an unrelated symbol move) must not destroy the cable line or its OTHER crossings.
        using var file = new TempSqliteFile();
        var (session, page, conn1, conn2) = SetUpTwoConnections(file);
        using var s = session;
        var result = s.DrawCableLine(page.Id, 10, -10, 10, 10, "-W1", [conn1, conn2]);

        s.DeleteConnection(conn1);

        var survivingLine = s.GetCableLines(page.Id).Single(l => l.Id == result.CableLineId);
        Assert.NotNull(survivingLine);
        var crossings = s.GetCableLineCrossings(result.CableLineId);
        Assert.Equal(2, crossings.Count);
        Assert.Contains(crossings, c => c.ConnectionId is null);
        Assert.Contains(crossings, c => c.ConnectionId == conn2);
    }

    [Fact]
    public void ReassignCableLine_ToADifferentCable_RehomesLiveCrossings()
    {
        using var file = new TempSqliteFile();
        var (session, page, conn1, _) = SetUpTwoConnections(file);
        using var s = session;
        var result = s.DrawCableLine(page.Id, 10, -10, 10, 10, "-W1", [conn1]);

        s.ReassignCableLine(result.CableLineId, "-W2", [conn1]);

        var conn1Updated = s.GetConnectionsForPage(page.Id).Single(c => c.Id == conn1);
        var newCable = s.GetAllCables().Single(c => c.Tag == "-W2");
        Assert.Equal(newCable.Id, conn1Updated.CableId);
        Assert.Single(s.GetCableCores(newCable.Id));
        var oldCable = s.GetAllCables().Single(c => c.Tag == "-W1");
        Assert.Empty(s.GetCableCores(oldCable.Id).Where(core => s.GetCableLineCrossings(result.CableLineId).Any(cr => cr.CableCoreId == core.Id)));
    }

    [Fact]
    public void SuggestNextCableTag_IncrementsAsCablesAreCreated()
    {
        using var file = new TempSqliteFile();
        var (session, page, conn1, conn2) = SetUpTwoConnections(file);
        using var s = session;

        Assert.Equal("-W1", s.SuggestNextCableTag());
        s.DrawCableLine(page.Id, 10, -10, 10, 10, s.SuggestNextCableTag(), [conn1]);
        Assert.Equal("-W2", s.SuggestNextCableTag());
        s.DrawCableLine(page.Id, 90, -10, 90, 10, s.SuggestNextCableTag(), [conn2]);
        Assert.Equal("-W3", s.SuggestNextCableTag());
    }

    [Fact]
    public void RotateCableLineCrossing_UpdatesRotation_DefaultsToZeroOnCreation()
    {
        using var file = new TempSqliteFile();
        var (session, page, conn1, _) = SetUpTwoConnections(file);
        using var s = session;
        var result = s.DrawCableLine(page.Id, 10, -10, 10, 10, "-W1", [conn1]);
        var crossing = s.GetCableLineCrossings(result.CableLineId).Single();
        Assert.Equal(0, crossing.RotationDegrees);

        s.RotateCableLineCrossing(crossing.Id, 90);

        var rotated = s.GetCableLineCrossings(result.CableLineId).Single();
        Assert.Equal(90, rotated.RotationDegrees);
    }

    [Fact]
    public void SetCableLineCrossingCore_UpdatesCoreNumberColorCrossSection_AndMirrorsOntoTheLiveConnection()
    {
        using var file = new TempSqliteFile();
        var (session, page, conn1, _) = SetUpTwoConnections(file);
        using var s = session;
        var result = s.DrawCableLine(page.Id, 10, -10, 10, 10, "-W1", [conn1]);
        var crossing = s.GetCableLineCrossings(result.CableLineId).Single();

        s.SetCableLineCrossingCore(crossing.Id, 5, "blue", 2.5);

        var core = s.GetCableCores(s.GetAllCables().Single().Id).Single();
        Assert.Equal(5, core.CoreNumber);
        Assert.Equal("blue", core.Color);
        Assert.Equal(2.5, core.CrossSectionMm2);
        var conn1Updated = s.GetConnectionsForPage(page.Id).Single(c => c.Id == conn1);
        Assert.Equal("blue", conn1Updated.Color);
        Assert.Equal(2.5, conn1Updated.CrossSectionMm2);
    }

    [Fact]
    public void IsCableCoreNumberAvailable_FalseWhenTaken_TrueWhenExcludingSelf()
    {
        using var file = new TempSqliteFile();
        var (session, page, conn1, conn2) = SetUpTwoConnections(file);
        using var s = session;
        var result = s.DrawCableLine(page.Id, 10, -10, 10, 10, "-W1", [conn1, conn2]);
        var crossings = s.GetCableLineCrossings(result.CableLineId).OrderBy(c => c.Id).ToList();
        var firstCore = s.GetCableCores(s.GetAllCables().Single().Id).Single(c => c.Id == crossings[0].CableCoreId);

        Assert.False(s.IsCableCoreNumberAvailable(s.GetAllCables().Single().Id, firstCore.CoreNumber, excludingCoreId: null));
        Assert.True(s.IsCableCoreNumberAvailable(s.GetAllCables().Single().Id, firstCore.CoreNumber, excludingCoreId: firstCore.Id));
    }
}
