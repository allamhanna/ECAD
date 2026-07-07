using Ecad.Core.Models;
using Xunit;

namespace Ecad.Data.Tests;

public class ProjectSessionConnectionTests
{
    [Fact]
    public void CreateConnection_AssignsWireNumberAndCreatesBothConnectionEnds()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var a = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, null, null, "X1");
        var b = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, null, null, "X2");
        var aPin = session.GetDevicePins(a.DeviceId).Single();
        var bPin = session.GetDevicePins(b.DeviceId).Single();

        var connection = session.CreateConnection(aPin.Id, bPin.Id);

        Assert.True(connection.Id > 0);
        Assert.Equal("1", connection.WireNumber);
    }

    [Fact]
    public void AreConnected_TrueBothDirections_FalseWhenNotConnected()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var a = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, null, null, "X1");
        var b = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, null, null, "X2");
        var c = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 80, 0, null, null, "X3");
        var aPin = session.GetDevicePins(a.DeviceId).Single();
        var bPin = session.GetDevicePins(b.DeviceId).Single();
        var cPin = session.GetDevicePins(c.DeviceId).Single();

        session.CreateConnection(aPin.Id, bPin.Id);

        Assert.True(session.AreConnected(aPin.Id, bPin.Id));
        Assert.True(session.AreConnected(bPin.Id, aPin.Id));
        Assert.False(session.AreConnected(aPin.Id, cPin.Id));
    }

    [Fact]
    public void DeleteConnection_RemovesIt()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var a = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, null, null, "X1");
        var b = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, null, null, "X2");
        var aPin = session.GetDevicePins(a.DeviceId).Single();
        var bPin = session.GetDevicePins(b.DeviceId).Single();
        var connection = session.CreateConnection(aPin.Id, bPin.Id);

        session.DeleteConnection(connection.Id);

        Assert.Empty(session.GetConnectionsForPage(page.Id));
    }

    [Fact]
    public void RenameWireNumber_UpdatesIt_AndUniquenessIsValidatedSeparately()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var a = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, null, null, "X1");
        var b = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, null, null, "X2");
        var aPin = session.GetDevicePins(a.DeviceId).Single();
        var bPin = session.GetDevicePins(b.DeviceId).Single();
        var connection = session.CreateConnection(aPin.Id, bPin.Id);

        Assert.False(session.IsWireNumberAvailable("1", excludingConnectionId: null));
        Assert.True(session.IsWireNumberAvailable("1", excludingConnectionId: connection.Id));

        session.RenameWireNumber(connection.Id, "W47");

        Assert.False(session.IsWireNumberAvailable("W47", excludingConnectionId: null));
        Assert.True(session.IsWireNumberAvailable("1", excludingConnectionId: null));
    }

    [Fact]
    public void SuggestNextWireNumber_IncrementsAcrossConnections()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var a = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, null, null, "X1");
        var b = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, null, null, "X2");
        var c = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 80, 0, null, null, "X3");
        var aPin = session.GetDevicePins(a.DeviceId).Single();
        var bPin = session.GetDevicePins(b.DeviceId).Single();
        var cPin = session.GetDevicePins(c.DeviceId).Single();

        Assert.Equal("1", session.SuggestNextWireNumber());
        session.CreateConnection(aPin.Id, bPin.Id);
        Assert.Equal("2", session.SuggestNextWireNumber());
        session.CreateConnection(bPin.Id, cPin.Id);
        Assert.Equal("3", session.SuggestNextWireNumber());
    }

    [Fact]
    public void RenumberAllWires_ReassignsSequentiallyByPageOrder()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var a = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, null, null, "X1");
        var b = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, null, null, "X2");
        var c = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 80, 0, null, null, "X3");
        var aPin = session.GetDevicePins(a.DeviceId).Single();
        var bPin = session.GetDevicePins(b.DeviceId).Single();
        var cPin = session.GetDevicePins(c.DeviceId).Single();
        var conn1 = session.CreateConnection(aPin.Id, bPin.Id);
        var conn2 = session.CreateConnection(bPin.Id, cPin.Id);
        session.RenameWireNumber(conn1.Id, "W99");
        session.RenameWireNumber(conn2.Id, "W1");

        var result = session.RenumberAllWires();

        Assert.Equal(2, result.Count);
        var byConnectionId = result.ToDictionary(r => r.ConnectionId);
        Assert.Equal("1", byConnectionId[conn1.Id].NewWireNumber);
        Assert.Equal("2", byConnectionId[conn2.Id].NewWireNumber);
        Assert.Equal("W99", byConnectionId[conn1.Id].OldWireNumber);
    }

    [Fact]
    public void DeletePlacement_WithAttachedConnection_DeletesConnectionFirst_NoForeignKeyViolation()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var a = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, null, null, "X1");
        var b = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, null, null, "X2");
        var aPin = session.GetDevicePins(a.DeviceId).Single();
        var bPin = session.GetDevicePins(b.DeviceId).Single();
        session.CreateConnection(aPin.Id, bPin.Id);

        var result = session.DeletePlacement(a.Id);

        Assert.True(result.DeviceDeleted);
        Assert.Empty(session.GetConnectionsForPage(page.Id));
        Assert.Null(session.GetDevice(a.DeviceId));
        // b survives — only its connection to the now-deleted a is gone.
        Assert.NotNull(session.GetDevice(b.DeviceId));
    }
}
