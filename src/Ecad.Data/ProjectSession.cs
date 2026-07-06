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
    private readonly List<Page> _pages;

    private ProjectSession(string filePath, SqliteConnection connection, Project project, List<Page> pages)
    {
        FilePath = filePath;
        _connection = connection;
        _projects = new ProjectRepository(connection);
        _placements = new PlacementRepository(connection);
        _devices = new DeviceRepository(connection);
        CurrentProject = project;
        _pages = pages;
    }

    public string FilePath { get; }
    public Project CurrentProject { get; private set; }
    public IReadOnlyList<Page> Pages => _pages;

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
    /// Places a symbol on a page: creates a Device (one placement per device in M5 — multi-placement
    /// devices are M6), a DevicePin per pin name, the Placement, and a PlacementPin per DevicePin.
    /// Also the first real writer to this project's Symbol table (ADR-006 left it unpopulated until
    /// a Placement actually needed a row to reference). Not wrapped in an explicit transaction —
    /// consistent with the rest of the codebase's per-statement-commit style for simple, fast writes.
    /// </summary>
    public Placement PlaceSymbol(long pageId, string symbolName, string? symbolLibraryName, string? symbolSvgFilePath,
        string? symbolCategory, IReadOnlyList<string> pinNames, double x, double y, string deviceTag)
    {
        var symbolId = _placements.GetOrCreateSymbol(symbolName, symbolLibraryName, symbolSvgFilePath, symbolCategory);

        var deviceId = _devices.InsertDevice(new Device { ProjectId = CurrentProject.Id, DeviceTagSegment = deviceTag });

        var devicePinIds = new List<long>();
        foreach (var pinName in pinNames)
            devicePinIds.Add(_devices.InsertDevicePin(new DevicePin { DeviceId = deviceId, Name = pinName }));

        var placement = new Placement { DeviceId = deviceId, PageId = pageId, SymbolId = symbolId, X = x, Y = y };
        placement.Id = _placements.InsertPlacement(placement);

        foreach (var devicePinId in devicePinIds)
            _placements.AddPlacementPin(placement.Id, devicePinId);

        return placement;
    }

    public void RenameDevice(long deviceId, string deviceTag) => _devices.UpdateDeviceTag(deviceId, deviceTag);

    public void MovePlacement(long placementId, double x, double y) => _placements.UpdatePosition(placementId, x, y);

    public void RotatePlacement(long placementId, int rotationDegrees, bool mirrored) =>
        _placements.UpdateRotation(placementId, rotationDegrees, mirrored);

    /// <summary>Deletes the placement's Device row; DevicePin/Placement/PlacementPin cascade per the M1 schema.</summary>
    public void DeletePlacement(long placementId)
    {
        var placement = _placements.GetPlacement(placementId)
            ?? throw new InvalidOperationException($"Placement {placementId} not found.");
        _devices.DeleteDevice(placement.DeviceId);
    }

    public IReadOnlyList<PlacementWithSymbol> GetPlacements(long pageId) => _placements.GetPlacementsForPage(pageId);

    public Device? GetDevice(long deviceId) => _devices.GetDevice(deviceId);

    public IReadOnlyList<DevicePin> GetDevicePins(long deviceId) => _devices.GetDevicePins(deviceId);

    public void Dispose() => _connection.Dispose();
}
