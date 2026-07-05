using Ecad.Core.Enums;
using Ecad.Core.Models;
using Ecad.Data.Repositories;
using Xunit;

namespace Ecad.Data.Tests;

public class ProjectSchemaTests
{
    [Fact]
    public void Project_And_Page_RoundTrip_ThroughGet()
    {
        using var file = new TempSqliteFile();
        using var connection = ProjectDatabase.Open(file.Path);
        var projects = new ProjectRepository(connection);

        var projectId = projects.InsertProject(new Project
        {
            Name = "Test Machine", Customer = "Acme", CreatedAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        });
        var pageId = projects.InsertPage(new Page
        {
            ProjectId = projectId, FunctionSegment = "K1", PageNumberSegment = "5",
            PageType = PageType.CableDrawing, SortOrder = 3,
        });

        var project = projects.GetProject(projectId)!;
        Assert.Equal("Test Machine", project.Name);
        Assert.Equal("Acme", project.Customer);

        var page = projects.GetPage(pageId)!;
        Assert.Equal(PageType.CableDrawing, page.PageType);
        Assert.Equal(3, page.SortOrder);
    }

    [Fact]
    public void MultiPlacementDevice_SharesDevicePinsAcrossPlacements_ForCrossReferences()
    {
        using var file = new TempSqliteFile();
        using var connection = ProjectDatabase.Open(file.Path);

        var projects = new ProjectRepository(connection);
        var devices = new DeviceRepository(connection);
        var placements = new PlacementRepository(connection);

        var projectId = projects.InsertProject(new Project { Name = "Test Machine", CreatedAtUtc = DateTimeOffset.UtcNow });
        var coilPageId = projects.InsertPage(new Page { ProjectId = projectId, FunctionSegment = "K1", PageNumberSegment = "5" });
        var contactPageId = projects.InsertPage(new Page { ProjectId = projectId, FunctionSegment = "K1", PageNumberSegment = "12" });
        var symbolId = placements.InsertSymbol(new Symbol { Name = "IEC_coil" });
        var contactSymbolId = placements.InsertSymbol(new Symbol { Name = "IEC_contact" });

        var deviceId = devices.InsertDevice(new Device { ProjectId = projectId, DeviceTagSegment = "K1" });
        var coilA1 = devices.InsertDevicePin(new DevicePin { DeviceId = deviceId, Name = "A1" });
        var coilA2 = devices.InsertDevicePin(new DevicePin { DeviceId = deviceId, Name = "A2" });
        var contact13 = devices.InsertDevicePin(new DevicePin { DeviceId = deviceId, Name = "13" });
        var contact14 = devices.InsertDevicePin(new DevicePin { DeviceId = deviceId, Name = "14" });

        var coilPlacementId = placements.InsertPlacement(new Placement
        {
            DeviceId = deviceId, PageId = coilPageId, SymbolId = symbolId, X = 10, Y = 20,
        });
        placements.AddPlacementPin(coilPlacementId, coilA1);
        placements.AddPlacementPin(coilPlacementId, coilA2);

        var contactPlacementId = placements.InsertPlacement(new Placement
        {
            DeviceId = deviceId, PageId = contactPageId, SymbolId = contactSymbolId, X = 30, Y = 40,
        });
        placements.AddPlacementPin(contactPlacementId, contact13);
        placements.AddPlacementPin(contactPlacementId, contact14);

        // The coil placement and the contact placement are different Placements of the same
        // Device, exposing disjoint DevicePins — the cross-reference link is "same Device",
        // not a shared pin, so each placement's sibling set is the other placement.
        var coilPins = devices.GetDevicePins(deviceId).Select(p => p.Name).ToList();
        Assert.Equal(new[] { "A1", "A2", "13", "14" }, coilPins);

        var coilPlacement = placements.GetPlacement(coilPlacementId)!;
        var contactPlacement = placements.GetPlacement(contactPlacementId)!;
        Assert.Equal(deviceId, coilPlacement.DeviceId);
        Assert.Equal(deviceId, contactPlacement.DeviceId);
        Assert.NotEqual(coilPlacement.PageId, contactPlacement.PageId);

        Assert.Equal(new[] { contactPlacementId }, placements.GetSiblingPlacementIds(coilPlacementId));
        Assert.Equal(new[] { coilPlacementId }, placements.GetSiblingPlacementIds(contactPlacementId));
    }

    [Fact]
    public void Connection_WithTerminationOnOneEndOnly_RoundTrips()
    {
        using var file = new TempSqliteFile();
        using var connection = ProjectDatabase.Open(file.Path);

        var projects = new ProjectRepository(connection);
        var devices = new DeviceRepository(connection);
        var connections = new ConnectionRepository(connection);

        var projectId = projects.InsertProject(new Project { Name = "Test Machine", CreatedAtUtc = DateTimeOffset.UtcNow });
        var deviceId = devices.InsertDevice(new Device { ProjectId = projectId, DeviceTagSegment = "K1" });
        var fromPin = devices.InsertDevicePin(new DevicePin { DeviceId = deviceId, Name = "A1" });
        var toPin = devices.InsertDevicePin(new DevicePin { DeviceId = deviceId, Name = "X1:1" });

        var connectionId = connections.InsertConnection(new Connection
        {
            FromDevicePinId = fromPin, ToDevicePinId = toPin, WireNumber = "W001", CrossSectionMm2 = 0.75,
        });

        connections.InsertConnectionEnd(new ConnectionEnd
        {
            ConnectionId = connectionId, End = ConnectionEndDesignator.From, TerminationEnabled = false,
        });
        connections.InsertConnectionEnd(new ConnectionEnd
        {
            ConnectionId = connectionId, End = ConnectionEndDesignator.To, TerminationEnabled = true,
            TerminationType = TerminationType.Ferrule, StrippingLengthMm = 8,
        });

        var ends = connections.GetConnectionEnds(connectionId);
        Assert.Equal(2, ends.Count);
        Assert.False(ends.Single(e => e.End == ConnectionEndDesignator.From).TerminationEnabled);
        var toEnd = ends.Single(e => e.End == ConnectionEndDesignator.To);
        Assert.True(toEnd.TerminationEnabled);
        Assert.Equal(TerminationType.Ferrule, toEnd.TerminationType);
        Assert.Equal(8, toEnd.StrippingLengthMm);
    }

    [Fact]
    public void Cable_WithCoresAndAssignedConnection_RoundTrips()
    {
        using var file = new TempSqliteFile();
        using var connection = ProjectDatabase.Open(file.Path);

        var projects = new ProjectRepository(connection);
        var devices = new DeviceRepository(connection);
        var connections = new ConnectionRepository(connection);
        var cables = new CableRepository(connection);

        var projectId = projects.InsertProject(new Project { Name = "Test Machine", CreatedAtUtc = DateTimeOffset.UtcNow });
        var deviceId = devices.InsertDevice(new Device { ProjectId = projectId, DeviceTagSegment = "M1" });
        var fromPin = devices.InsertDevicePin(new DevicePin { DeviceId = deviceId, Name = "U" });
        var toPin = devices.InsertDevicePin(new DevicePin { DeviceId = deviceId, Name = "1" });

        var cableId = cables.InsertCable(new Cable { Tag = "-W12", EndTypeClassification = "FER-FER" });
        var coreId = cables.InsertCableCore(new CableCore { CableId = cableId, CoreNumber = 1, Color = "BN", CrossSectionMm2 = 1.0 });

        var connectionId = connections.InsertConnection(new Connection
        {
            FromDevicePinId = fromPin, ToDevicePinId = toPin, CableId = cableId, CableCoreId = coreId,
        });

        var cores = cables.GetCableCores(cableId);
        Assert.Single(cores);
        Assert.Equal("BN", cores[0].Color);

        var savedConnection = connections.GetConnection(connectionId)!;
        Assert.Equal(cableId, savedConnection.CableId);
        Assert.Equal(coreId, savedConnection.CableCoreId);
    }

    [Fact]
    public void Udp_DefinitionAndValue_AttachToDevice()
    {
        using var file = new TempSqliteFile();
        using var connection = ProjectDatabase.Open(file.Path);

        var projects = new ProjectRepository(connection);
        var devices = new DeviceRepository(connection);
        var udps = new UdpRepository(connection);

        var projectId = projects.InsertProject(new Project { Name = "Test Machine", CreatedAtUtc = DateTimeOffset.UtcNow });
        var deviceId = devices.InsertDevice(new Device { ProjectId = projectId, DeviceTagSegment = "K1" });

        var definitionId = udps.InsertDefinition(new UdpDefinition
        {
            Name = "CableEndType", DataType = UdpDataType.Enum,
            EnumValuesJson = "[\"FER-FER\",\"FER-CONN\"]", AppliesToEntityType = UdpEntityType.Device,
        });
        udps.InsertValue(new UdpValue { DefinitionId = definitionId, EntityType = UdpEntityType.Device, EntityId = deviceId, Value = "FER-CONN" });

        var values = udps.GetValuesForEntity(UdpEntityType.Device, deviceId);
        Assert.Single(values);
        Assert.Equal("FER-CONN", values[0].Value);
    }
}
