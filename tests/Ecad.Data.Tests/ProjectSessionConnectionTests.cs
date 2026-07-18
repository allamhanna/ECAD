using Ecad.Core.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Ecad.Data.Tests;

public class ProjectSessionConnectionTests
{
    [Fact]
    public void CreateConnection_HasNoWireNumber_ButCreatesBothConnectionEnds()
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
        Assert.Null(connection.WireNumber);
        Assert.Empty(session.GetDefinitionPoints(page.Id));
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
    public void GetConnectionPage_ResolvesPageAndOneEndpointPlacement()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var a = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, null, null, "X1");
        var b = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, null, null, "X2");
        var aPin = session.GetDevicePins(a.DeviceId).Single();
        var bPin = session.GetDevicePins(b.DeviceId).Single();
        var connection = session.CreateConnection(aPin.Id, bPin.Id);

        var target = session.GetConnectionPage(connection.Id);

        Assert.NotNull(target);
        Assert.Equal(page.Id, target!.PageId);
        Assert.Equal(a.Id, target.PlacementId);
    }

    [Fact]
    public void GetDefinitionPointForConnection_ReturnsAttachedPoint_NullWhenBare()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var a = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, null, null, "X1");
        var b = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, null, null, "X2");
        var c = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 80, 0, null, null, "X3");
        var d = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 120, 0, null, null, "X4");
        var aPin = session.GetDevicePins(a.DeviceId).Single();
        var bPin = session.GetDevicePins(b.DeviceId).Single();
        var cPin = session.GetDevicePins(c.DeviceId).Single();
        var dPin = session.GetDevicePins(d.DeviceId).Single();
        var withPoint = session.CreateConnection(aPin.Id, bPin.Id);
        var bare = session.CreateConnection(cPin.Id, dPin.Id);
        var point = session.PlaceDefinitionPoint(page.Id, 20, 0, "1", "red", 1.5, withPoint.Id);

        Assert.Equal(point.Id, session.GetDefinitionPointForConnection(withPoint.Id)!.Id);
        Assert.Null(session.GetDefinitionPointForConnection(bare.Id));
    }

    [Fact]
    public void PlaceDefinitionPoint_AttachedToConnection_SetsAllFields_AndMirrorsOntoTheConnection()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var a = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, null, null, "X1");
        var b = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, null, null, "X2");
        var aPin = session.GetDevicePins(a.DeviceId).Single();
        var bPin = session.GetDevicePins(b.DeviceId).Single();
        var connection = session.CreateConnection(aPin.Id, bPin.Id);

        var point = session.PlaceDefinitionPoint(page.Id, 20, 0, "1", "red", 1.5, connection.Id);

        Assert.True(point.Id > 0);
        Assert.Equal(connection.Id, point.ConnectionId);
        var mirrored = session.GetConnectionsForPage(page.Id).Single(c => c.Id == connection.Id);
        Assert.Equal("1", mirrored.WireNumber);
        Assert.Equal("red", mirrored.Color);
        Assert.Equal(1.5, mirrored.CrossSectionMm2);

        Assert.False(session.IsWireNumberAvailable("1", excludingDefinitionPointId: null));
        Assert.True(session.IsWireNumberAvailable("1", excludingDefinitionPointId: point.Id));

        session.SetDefinitionPointData(point.Id, "W47", "red", 1.5);

        Assert.False(session.IsWireNumberAvailable("W47", excludingDefinitionPointId: null));
        Assert.True(session.IsWireNumberAvailable("1", excludingDefinitionPointId: null));
        var afterEdit = session.GetConnectionsForPage(page.Id).Single(c => c.Id == connection.Id);
        Assert.Equal("W47", afterEdit.WireNumber);
    }

    [Fact]
    public void PlaceDefinitionPoint_WithNoConnection_IsFreeFloating_AndDoesNotMirrorAnywhere()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });

        var point = session.PlaceDefinitionPoint(page.Id, 100, 100, "1", "blue", 0.75, null);

        Assert.Null(point.ConnectionId);
        var reloaded = session.GetDefinitionPoints(page.Id).Single();
        Assert.Equal(100, reloaded.X);
        Assert.Equal(100, reloaded.Y);
        Assert.Equal("1", reloaded.WireNumber);
    }

    [Fact]
    public void DeleteDefinitionPoint_RemovesTheRow_AndClearsTheMirroredConnectionFields()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var a = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, null, null, "X1");
        var b = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, null, null, "X2");
        var aPin = session.GetDevicePins(a.DeviceId).Single();
        var bPin = session.GetDevicePins(b.DeviceId).Single();
        var connection = session.CreateConnection(aPin.Id, bPin.Id);
        var point = session.PlaceDefinitionPoint(page.Id, 20, 0, "1", "red", 1.5, connection.Id);

        session.DeleteDefinitionPoint(point.Id);

        Assert.Empty(session.GetDefinitionPoints(page.Id));
        var cleared = session.GetConnectionsForPage(page.Id).Single(c => c.Id == connection.Id);
        Assert.Null(cleared.WireNumber);
        Assert.Null(cleared.Color);
        Assert.Null(cleared.CrossSectionMm2);
        Assert.True(session.IsWireNumberAvailable("1", excludingDefinitionPointId: null));
    }

    [Fact]
    public void DeleteConnection_WithAttachedDefinitionPoint_DetachesButPreservesThePoint()
    {
        // The regression this whole entity redesign exists to fix: moving a connected symbol makes
        // auto-connect delete and recreate the Connection row — the definition point must survive
        // that, unattached, with its own position/data completely untouched.
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var a = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, null, null, "X1");
        var b = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, null, null, "X2");
        var aPin = session.GetDevicePins(a.DeviceId).Single();
        var bPin = session.GetDevicePins(b.DeviceId).Single();
        var connection = session.CreateConnection(aPin.Id, bPin.Id);
        var point = session.PlaceDefinitionPoint(page.Id, 20, 0, "1", "red", 1.5, connection.Id);

        session.DeleteConnection(connection.Id);

        var survivor = session.GetDefinitionPoints(page.Id).Single(p => p.Id == point.Id);
        Assert.Null(survivor.ConnectionId);
        Assert.Equal(20, survivor.X);
        Assert.Equal(0, survivor.Y);
        Assert.Equal("1", survivor.WireNumber);
        Assert.Equal("red", survivor.Color);
        Assert.Equal(1.5, survivor.CrossSectionMm2);
    }

    [Fact]
    public void AttachDefinitionPointToConnection_OntoAnAlreadyAttachedConnection_ThrowsUniqueConstraintViolation()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var a = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 0, 0, null, null, "X1");
        var b = session.PlaceSymbol(page.Id, "Terminal", null, "SymbolLibrary/Terminal.svg", "Terminals", ["1"], 40, 0, null, null, "X2");
        var aPin = session.GetDevicePins(a.DeviceId).Single();
        var bPin = session.GetDevicePins(b.DeviceId).Single();
        var connection = session.CreateConnection(aPin.Id, bPin.Id);
        session.PlaceDefinitionPoint(page.Id, 20, 0, "1", null, null, connection.Id);
        var secondPoint = session.PlaceDefinitionPoint(page.Id, 100, 100, "2", null, null, null);

        Assert.Throws<SqliteException>(() => session.AttachDefinitionPointToConnection(secondPoint.Id, connection.Id));
    }

    [Fact]
    public void SuggestNextWireNumber_IncrementsAsDefinitionPointsAreSet()
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

        // No definition points yet — CreateConnection no longer auto-assigns a number.
        Assert.Equal("1", session.SuggestNextWireNumber());
        session.PlaceDefinitionPoint(page.Id, 20, 0, session.SuggestNextWireNumber(), null, null, conn1.Id);
        Assert.Equal("2", session.SuggestNextWireNumber());
        session.PlaceDefinitionPoint(page.Id, 60, 0, session.SuggestNextWireNumber(), null, null, conn2.Id);
        Assert.Equal("3", session.SuggestNextWireNumber());
    }

    [Fact]
    public void RenumberAllWires_ReassignsSequentiallyByPageOrder_SkippingDefinitionPointsWithNoWireNumber()
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
        var point1 = session.PlaceDefinitionPoint(page.Id, 20, 0, "W99", null, null, conn1.Id);
        var point2 = session.PlaceDefinitionPoint(page.Id, 60, 0, "W1", null, null, conn2.Id);
        var undefinedPoint = session.PlaceDefinitionPoint(page.Id, 100, 100, null, null, null, null); // no wire number — should be skipped

        var result = session.RenumberAllWires();

        Assert.Equal(2, result.Count);
        var byDefinitionPointId = result.ToDictionary(r => r.DefinitionPointId);
        Assert.Equal("1", byDefinitionPointId[point1.Id].NewWireNumber);
        Assert.Equal("2", byDefinitionPointId[point2.Id].NewWireNumber);
        Assert.Equal("W99", byDefinitionPointId[point1.Id].OldWireNumber);
        Assert.False(byDefinitionPointId.ContainsKey(undefinedPoint.Id));
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

    [Fact]
    public void RotateDefinitionPoint_UpdatesRotation_DefaultsToZeroOnPlacement()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test", CreatedAtUtc = DateTimeOffset.UtcNow });
        var page = session.AddPage(new Page { PageNumberSegment = "1" });
        var point = session.PlaceDefinitionPoint(page.Id, 20, 0, "1", null, null, null);
        Assert.Equal(0, point.RotationDegrees);

        session.RotateDefinitionPoint(point.Id, 90);

        var rotated = session.GetDefinitionPoints(page.Id).Single(p => p.Id == point.Id);
        Assert.Equal(90, rotated.RotationDegrees);
    }
}
