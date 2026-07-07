using System.Text.RegularExpressions;
using Ecad.Core.Models;
using Ecad.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace Ecad.Data;

/// <summary>
/// An open project: the SQLite connection to its .ecad file plus the currently loaded Project
/// row and its Pages. Plain C#, no UI dependency, so it's unit-testable — Ecad.App's
/// MainViewModel is a thin wrapper over this that adds file-picker dialogs.
/// </summary>
public sealed class ProjectSession : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ProjectRepository _projects;
    private readonly PlacementRepository _placements;
    private readonly DeviceRepository _devices;
    private readonly ConnectionRepository _connections;
    private readonly List<Page> _pages;

    private ProjectSession(string filePath, SqliteConnection connection, Project project, List<Page> pages)
    {
        FilePath = filePath;
        _connection = connection;
        _projects = new ProjectRepository(connection);
        _placements = new PlacementRepository(connection);
        _devices = new DeviceRepository(connection);
        _connections = new ConnectionRepository(connection);
        CurrentProject = project;
        _pages = pages;
    }

    public string FilePath { get; }
    public Project CurrentProject { get; private set; }
    public IReadOnlyList<Page> Pages => _pages;

    /// <summary>
    /// Raised after a Placement is added, removed, or a Device's tag is renamed — the operations
    /// that can change a multi-placement Device's cross-reference set or displayed tag (Section 5.4,
    /// 6.1's "rendered live"). One ProjectSession is shared by every open SchematicPageWindow for a
    /// project, so this is how a change made in one window's page reaches another window showing a
    /// sibling placement of the same Device — without it, that other window's cross-reference text
    /// and tag would go stale until its page was reopened.
    /// </summary>
    public event Action? PlacementsChanged;

    /// <summary>Raised after a Connection is created, deleted, or renamed — the M7 analog of
    /// PlacementsChanged, same reuse-the-shared-session-event pattern (see ADR-008/ADR-009).</summary>
    public event Action? ConnectionsChanged;

    /// <summary>Creates a new .ecad file at the given path and inserts the given Project as its single Project row.</summary>
    public static ProjectSession Create(string filePath, Project project)
    {
        var connection = ProjectDatabase.Open(filePath);
        var projects = new ProjectRepository(connection);
        project.Id = projects.InsertProject(project);
        return new ProjectSession(filePath, connection, project, []);
    }

    /// <summary>Opens an existing .ecad file and loads its Project row and Pages.</summary>
    public static ProjectSession Open(string filePath)
    {
        var connection = ProjectDatabase.Open(filePath);
        try
        {
            var projects = new ProjectRepository(connection);
            var project = projects.GetFirstProject()
                ?? throw new InvalidOperationException($"'{filePath}' has no Project row.");
            var pages = projects.GetPages(project.Id).ToList();
            return new ProjectSession(filePath, connection, project, pages);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public Page AddPage(Page page)
    {
        page.ProjectId = CurrentProject.Id;
        page.Id = _projects.InsertPage(page);
        _pages.Add(page);
        return page;
    }

    /// <summary>Flushes any WAL contents to the main database file. Writes already commit immediately
    /// on each repository call — this exists so File &gt; Save is a real, truthful action.</summary>
    public void Checkpoint()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint;";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Copies the current project file to a new path and returns a session backed by that new
    /// file. This session's connection is disposed as part of the switch — the caller should
    /// replace its reference with the returned session and not use this instance afterward.
    /// </summary>
    public ProjectSession SaveAs(string newFilePath)
    {
        if (string.Equals(Path.GetFullPath(newFilePath), Path.GetFullPath(FilePath), StringComparison.OrdinalIgnoreCase))
        {
            Checkpoint();
            return this;
        }

        Checkpoint();
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
        File.Copy(FilePath, newFilePath, overwrite: true);
        return Open(newFilePath);
    }

    /// <summary>
    /// Places a symbol on a page as a brand-new Device: creates the Device, a DevicePin per pin
    /// name, the Placement, and a PlacementPin per DevicePin. Also the first real writer to this
    /// project's Symbol table (ADR-006 left it unpopulated until a Placement actually needed a row
    /// to reference). Not wrapped in an explicit transaction — consistent with the rest of the
    /// codebase's per-statement-commit style for simple, fast writes.
    /// </summary>
    public Placement PlaceSymbol(long pageId, string symbolName, string? symbolLibraryName, string? symbolSvgFilePath,
        string? symbolCategory, IReadOnlyList<string> pinNames, double x, double y,
        string? function, string? location, string deviceTag)
    {
        var deviceId = _devices.InsertDevice(new Device
        {
            ProjectId = CurrentProject.Id, FunctionSegment = function, LocationSegment = location, DeviceTagSegment = deviceTag,
        });
        return PlaceSymbolOnDevice(deviceId, pageId, symbolName, symbolLibraryName, symbolSvgFilePath, symbolCategory, pinNames, x, y);
    }

    /// <summary>
    /// Places a symbol on a page as another Placement of an EXISTING Device (M6: multi-placement
    /// devices — e.g. a relay's contact block placed on a different page than its coil). New
    /// DevicePins are created for this placement's pins and added to the existing Device; its tag
    /// is inherited, not re-prompted.
    /// </summary>
    public Placement PlaceSymbolOnExistingDevice(long deviceId, long pageId, string symbolName, string? symbolLibraryName,
        string? symbolSvgFilePath, string? symbolCategory, IReadOnlyList<string> pinNames, double x, double y) =>
        PlaceSymbolOnDevice(deviceId, pageId, symbolName, symbolLibraryName, symbolSvgFilePath, symbolCategory, pinNames, x, y);

    private Placement PlaceSymbolOnDevice(long deviceId, long pageId, string symbolName, string? symbolLibraryName,
        string? symbolSvgFilePath, string? symbolCategory, IReadOnlyList<string> pinNames, double x, double y)
    {
        var symbolId = _placements.GetOrCreateSymbol(symbolName, symbolLibraryName, symbolSvgFilePath, symbolCategory);

        var devicePinIds = new List<long>();
        foreach (var pinName in pinNames)
            devicePinIds.Add(_devices.InsertDevicePin(new DevicePin { DeviceId = deviceId, Name = pinName }));

        var placement = new Placement { DeviceId = deviceId, PageId = pageId, SymbolId = symbolId, X = x, Y = y };
        placement.Id = _placements.InsertPlacement(placement);

        foreach (var devicePinId in devicePinIds)
            _placements.AddPlacementPin(placement.Id, devicePinId);

        PlacementsChanged?.Invoke();
        return placement;
    }

    public void RenameDeviceTag(long deviceId, string? function, string? location, string deviceTag)
    {
        _devices.UpdateDeviceTag(deviceId, function, location, deviceTag);
        PlacementsChanged?.Invoke();
    }

    public void MovePlacement(long placementId, double x, double y) => _placements.UpdatePosition(placementId, x, y);

    public void RotatePlacement(long placementId, int rotationDegrees, bool mirrored) =>
        _placements.UpdateRotation(placementId, rotationDegrees, mirrored);

    /// <summary>
    /// Deletes a placement: any Connections touching one of its pins are deleted first (M7:
    /// Connection's FKs to DevicePin have no ON DELETE CASCADE, so this must happen before the pins
    /// themselves go, or the delete throws — a real bug found while building wiring, since M6 never
    /// had to consider a pin having a dependent Connection). DevicePins exposed only by this
    /// placement are then removed (a sibling placement's pins are left alone), then the Placement
    /// itself, then the Device too — but only if this was its last remaining Placement (M6: a Device
    /// can have several). The returned result tells the caller (DeleteCommand) which branch was
    /// taken, so undo can either recreate a whole new Device (ADR-007's original recreate-not-restore
    /// strategy) or just a new Placement on the Device that's still there — note this means undoing
    /// the delete of a wired placement brings the placement back but not its wires (ADR-009).
    /// </summary>
    public PlacementDeletionResult DeletePlacement(long placementId)
    {
        var placement = _placements.GetPlacement(placementId)
            ?? throw new InvalidOperationException($"Placement {placementId} not found.");
        var device = _devices.GetDevice(placement.DeviceId)
            ?? throw new InvalidOperationException($"Device {placement.DeviceId} not found.");
        var pins = _placements.GetPlacementPins(placementId);
        var pinNames = pins.Select(p => p.Name).ToList();

        var anyConnectionsDeleted = false;
        foreach (var pin in pins)
        {
            foreach (var conn in _connections.GetConnectionsForDevicePin(pin.DevicePinId))
            {
                _connections.DeleteConnection(conn.Id);
                anyConnectionsDeleted = true;
            }
        }

        _placements.DeleteExclusiveDevicePinsForPlacement(placementId);
        _placements.DeletePlacement(placementId);

        var deviceDeleted = _placements.CountPlacementsForDevice(device.Id) == 0;
        if (deviceDeleted) _devices.DeleteDevice(device.Id);

        PlacementsChanged?.Invoke();
        if (anyConnectionsDeleted) ConnectionsChanged?.Invoke();
        return new PlacementDeletionResult(deviceDeleted, device.Id, device.DeviceTagSegment, device.FunctionSegment, device.LocationSegment, pinNames);
    }

    /// <summary>All placements on a page, with each one's sibling placements (Section 5.4 cross-reference
    /// display) and exposed pins (M7: for wire endpoint resolution) attached.</summary>
    public IReadOnlyList<PlacementWithSymbol> GetPlacements(long pageId)
    {
        var placements = _placements.GetPlacementsForPage(pageId);
        foreach (var placement in placements)
        {
            placement.Siblings = _placements.GetSiblingPlacementRefs(placement.PlacementId);
            placement.Pins = _placements.GetPlacementPins(placement.PlacementId);
        }
        return placements;
    }

    /// <summary>
    /// Creates a Connection between two DevicePins (Section 5.5) with a fresh auto-assigned wire
    /// number and both ConnectionEnds (terminations off by default — Phase 1's "optional, toggleable
    /// per end"). Used for both manual wire-drawing and auto-connect (Section 6.1: "touching a symbol
    /// pin creates a Connection"); the caller resolves whether two pins are actually touching (a pure,
    /// symbol-geometry-aware concern — see Ecad.Rendering.Canvas.AutoConnectDetector) before calling this.
    /// </summary>
    public Connection CreateConnection(long fromDevicePinId, long toDevicePinId)
    {
        var connection = new Connection
        {
            FromDevicePinId = fromDevicePinId,
            ToDevicePinId = toDevicePinId,
            WireNumber = SuggestNextWireNumber(),
        };
        connection.Id = _connections.InsertConnection(connection);
        _connections.InsertConnectionEnd(new ConnectionEnd { ConnectionId = connection.Id, End = Core.Enums.ConnectionEndDesignator.From });
        _connections.InsertConnectionEnd(new ConnectionEnd { ConnectionId = connection.Id, End = Core.Enums.ConnectionEndDesignator.To });

        ConnectionsChanged?.Invoke();
        return connection;
    }

    public void DeleteConnection(long connectionId)
    {
        _connections.DeleteConnection(connectionId);
        ConnectionsChanged?.Invoke();
    }

    public IReadOnlyList<Connection> GetConnectionsForPage(long pageId) => _connections.GetConnectionsForPage(pageId);

    public bool AreConnected(long devicePinIdA, long devicePinIdB) => _connections.AreConnected(devicePinIdA, devicePinIdB);

    public void RenameWireNumber(long connectionId, string wireNumber)
    {
        _connections.UpdateWireNumber(connectionId, wireNumber);
        ConnectionsChanged?.Invoke();
    }

    public bool IsWireNumberAvailable(string wireNumber, long? excludingConnectionId) =>
        _connections.FindByWireNumber(CurrentProject.Id, wireNumber, excludingConnectionId) is null;

    /// <summary>Simple sequential suggestion, project-wide (max existing integer + 1) — same
    /// simplification precedent as DeviceRepository.SuggestNextDesignation (ADR-008/ADR-009):
    /// not the "configurable per-page-or-project" scheme Section 6.1 mentions, just a starting
    /// point the user can freely overwrite.</summary>
    public string SuggestNextWireNumber()
    {
        var maxNumber = 0;
        foreach (var wireNumber in _connections.GetAllWireNumbers(CurrentProject.Id))
        {
            var match = Regex.Match(wireNumber, @"(\d+)$");
            if (match.Success && int.Parse(match.Value) > maxNumber) maxNumber = int.Parse(match.Value);
        }
        return (maxNumber + 1).ToString();
    }

    /// <summary>Reassigns every Connection in the project fresh sequential wire numbers, ordered by
    /// page SortOrder then Connection Id (Section 6.1: "renumber command available"). Returns the
    /// old-&gt;new number mapping so the caller can build an undoable command from it.</summary>
    public IReadOnlyList<(long ConnectionId, string? OldWireNumber, string NewWireNumber)> RenumberAllWires()
    {
        var connectionIds = _connections.GetConnectionIdsForRenumbering(CurrentProject.Id);
        var results = new List<(long, string?, string)>();

        var number = 1;
        foreach (var connectionId in connectionIds)
        {
            var oldWireNumber = _connections.GetConnection(connectionId)?.WireNumber;
            var newWireNumber = number.ToString();
            _connections.UpdateWireNumber(connectionId, newWireNumber);
            results.Add((connectionId, oldWireNumber, newWireNumber));
            number++;
        }

        ConnectionsChanged?.Invoke();
        return results;
    }

    /// <summary>Reverts a RenumberAllWires() call using its returned mapping — for undo.</summary>
    public void ApplyWireNumbers(IReadOnlyList<(long ConnectionId, string? WireNumber)> assignments)
    {
        foreach (var (connectionId, wireNumber) in assignments)
            _connections.UpdateWireNumber(connectionId, wireNumber);
        ConnectionsChanged?.Invoke();
    }

    public Device? GetDevice(long deviceId) => _devices.GetDevice(deviceId);

    public IReadOnlyList<DevicePin> GetDevicePins(long deviceId) => _devices.GetDevicePins(deviceId);

    public IReadOnlyList<PlacementPinInfo> GetPlacementPins(long placementId) => _placements.GetPlacementPins(placementId);

    public IReadOnlyList<Device> GetAllDevices() => _devices.GetAllDevices(CurrentProject.Id);

    public string SuggestNextDesignation(string? function, string? location) =>
        _devices.SuggestNextDesignation(CurrentProject.Id, function, location);

    public bool IsTagAvailable(string? function, string? location, string deviceTag, long? excludingDeviceId) =>
        _devices.FindByTag(CurrentProject.Id, function, location, deviceTag, excludingDeviceId) is null;

    public void Dispose() => _connection.Dispose();
}
