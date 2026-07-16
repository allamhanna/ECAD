using System.Text.RegularExpressions;
using Ecad.Core.Enums;
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
    private readonly DefinitionPointRepository _definitionPoints;
    private readonly CableRepository _cables;
    private readonly CableLineRepository _cableLines;
    private readonly PartRepository _parts;
    private readonly GeneratedReportRepository _generatedReports;
    private readonly List<Page> _pages;

    private ProjectSession(string filePath, SqliteConnection connection, Project project, List<Page> pages)
    {
        FilePath = filePath;
        _connection = connection;
        _projects = new ProjectRepository(connection);
        _placements = new PlacementRepository(connection);
        _devices = new DeviceRepository(connection);
        _connections = new ConnectionRepository(connection);
        _definitionPoints = new DefinitionPointRepository(connection);
        _cables = new CableRepository(connection);
        _cableLines = new CableLineRepository(connection);
        _parts = new PartRepository(connection);
        _generatedReports = new GeneratedReportRepository(connection);
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

    /// <summary>Raised after a Cable or CableCore is created, edited, or deleted — the M8 analog of
    /// PlacementsChanged/ConnectionsChanged, for the Grid Editor's Cables tab and the Connections
    /// tab's Cable/CableCore pickers.</summary>
    public event Action? CablesChanged;

    /// <summary>Raised after a DefinitionPoint is placed, moved, edited, attached/detached, or
    /// deleted — same cross-window live-sync pattern as PlacementsChanged/ConnectionsChanged/
    /// CablesChanged.</summary>
    public event Action? DefinitionPointsChanged;

    /// <summary>Raised after a CableLine is drawn, moved, re-homed to a different cable, or deleted —
    /// same cross-window live-sync pattern as the other *Changed events.</summary>
    public event Action? CableLinesChanged;

    /// <summary>Raised after a Page is added, updated, or deleted — M12: a generated report page
    /// appearing/disappearing (or being reused on regeneration) needs to reach MainViewModel's Pages
    /// list and any open tab, same cross-window live-sync pattern as the other *Changed events.</summary>
    public event Action? PagesChanged;

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

    /// <summary>Edits a page's own segments/type in place (the Pages sidebar's "Rename" action) — a
    /// single-page-only operation, matching Rotate's existing single-select-only scoping, since a
    /// Page's identity fields (especially PageNumberSegment) are inherently per-page data, not something
    /// several pages should be bulk-set to the same value.</summary>
    public void RenamePage(long pageId, string? function, string? location, string? documentType, string pageNumber, PageType pageType)
    {
        var page = _pages.Single(p => p.Id == pageId);
        page.FunctionSegment = function;
        page.LocationSegment = location;
        page.DocumentTypeSegment = documentType;
        page.PageNumberSegment = pageNumber;
        page.PageType = pageType;
        _projects.UpdatePage(page);
        PagesChanged?.Invoke();
    }

    /// <summary>Auto-sequences PageNumberSegment as 1, 2, 3... across exactly the given pages, in the
    /// order given (the Pages sidebar's "Renumber" action) — same auto-sequential convention as
    /// RenumberAllWires, just scoped to a specific selection instead of the whole project.</summary>
    public void RenumberPages(IReadOnlyList<long> pageIdsInOrder)
    {
        var number = 1;
        foreach (var pageId in pageIdsInOrder)
        {
            var page = _pages.Single(p => p.Id == pageId);
            page.PageNumberSegment = number.ToString();
            _projects.UpdatePage(page);
            number++;
        }
        PagesChanged?.Invoke();
    }

    /// <summary>
    /// Deletes one or more pages entirely, along with everything drawn on them: every Placement (and,
    /// via the same DeletePlacementCore path DeleteDeviceCascade already uses, each Placement's Device if
    /// this was its last one, plus any dependent Connections), while DefinitionPoint/CableLine/
    /// CableLineCrossing/GeneratedReport rows cascade at the SQL level (all ON DELETE CASCADE from
    /// PageId). Reuses DeletePlacementCore rather than a raw cascading DELETE FROM Page so a Device that
    /// loses its last Placement here is cleaned up exactly the same way DeleteDeviceCascade already
    /// handles it — a plain SQL cascade would silently orphan that Device instead.
    /// </summary>
    public void DeletePagesCascade(IReadOnlyList<long> pageIds)
    {
        var anyConnectionsDeleted = false;
        foreach (var pageId in pageIds)
        {
            var placementIds = _placements.GetPlacementsForPage(pageId).Select(p => p.PlacementId).ToList();
            foreach (var placementId in placementIds)
            {
                var (_, connectionsDeletedForThisPlacement) = DeletePlacementCore(placementId);
                anyConnectionsDeleted |= connectionsDeletedForThisPlacement;
            }

            _projects.DeletePage(pageId);
            _pages.RemoveAll(p => p.Id == pageId);
        }

        PlacementsChanged?.Invoke();
        if (anyConnectionsDeleted) ConnectionsChanged?.Invoke();
        DefinitionPointsChanged?.Invoke();
        CableLinesChanged?.Invoke();
        PagesChanged?.Invoke();
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

    /// <summary>M8: finally writes to Device.PartId, unused since M1 — deferred out of M6 as "likely M8".
    /// partId must already be a PROJECT-LOCAL Part.Id (i.e. the return value of EnsurePartCached) —
    /// passing a Library-DB Part.Id here throws a foreign key violation, since Device.PartId only ever
    /// references this project's own cached Part table (ADR-003), never the shared Library DB directly.</summary>
    public void UpdateDevicePart(long deviceId, long? partId)
    {
        _devices.UpdateDevicePart(deviceId, partId);
        PlacementsChanged?.Invoke();
    }

    /// <summary>M8 bulk edit: assigns one Part to several Devices at once (Section 6.2's own example),
    /// raising PlacementsChanged once for the whole batch rather than once per device. Same
    /// project-local-Id requirement as UpdateDevicePart — see EnsurePartCached.</summary>
    public void BulkUpdateDevicePart(IReadOnlyList<long> deviceIds, long? partId)
    {
        foreach (var deviceId in deviceIds)
            _devices.UpdateDevicePart(deviceId, partId);
        PlacementsChanged?.Invoke();
    }

    /// <summary>
    /// ADR-012: ensures the given Library-DB Part — identified by its Library-local Id — has an
    /// up-to-date cached copy in THIS project's own database (ADR-003's "Project DB keeps a local
    /// cached copy of any Part a Device references", never actually implemented until now since M8 is
    /// the first feature to touch Device.PartId), and returns that copy's PROJECT-LOCAL Id — the only
    /// Id valid for UpdateDevicePart/BulkUpdateDevicePart. Manufacturer/Supplier Organizations are
    /// resolved-or-created in the project DB by ExternalKey (Organization.Id is not portable across
    /// databases either). Classification is left unmapped — nothing populates it anywhere yet (M3).
    /// libraryFilePath overrides the default %LOCALAPPDATA% library location — null in production
    /// (LibraryDatabase.Open's own default), set by tests to point at an isolated TempSqliteFile.
    /// </summary>
    public long EnsurePartCached(long libraryPartId, string? libraryFilePath = null)
    {
        using var libraryConnection = LibraryDatabase.Open(libraryFilePath);
        var libraryParts = new PartRepository(libraryConnection);
        var part = libraryParts.GetPart(libraryPartId)
            ?? throw new InvalidOperationException($"Part {libraryPartId} not found in the library.");

        part.ManufacturerId = ResolveOrganizationIntoProject(libraryParts, part.ManufacturerId);
        part.SupplierId = ResolveOrganizationIntoProject(libraryParts, part.SupplierId);
        part.ClassificationId = null;
        part.SourceImportBatchId = null;

        _parts.UpsertByExternalKey(part, DateTimeOffset.UtcNow);
        return part.Id;
    }

    private long? ResolveOrganizationIntoProject(PartRepository libraryParts, long? libraryOrganizationId)
    {
        if (libraryOrganizationId is null) return null;
        var organization = libraryParts.GetOrganization(libraryOrganizationId.Value);
        if (organization?.ExternalKey is null) return null;
        return _parts.GetOrCreateOrganization(organization.Name, organization.ExternalKey);
    }

    /// <summary>Looks up a Part already cached in this project's own database (see EnsurePartCached) —
    /// used to display, e.g., the ExternalKey of a Device's assigned Part without needing the Library DB.</summary>
    public Part? GetCachedPart(long partId) => _parts.GetPart(partId);

    /// <summary>M12: every Part cached in this project's own database — report Builders (BOM,
    /// connection list) need the whole set at once rather than one lookup per reference.</summary>
    public IReadOnlyList<Part> GetAllParts() => _parts.GetAllParts();

    public long AddDevicePin(long deviceId, string name, string? function, string? technicalData)
    {
        var pinId = _devices.InsertDevicePin(new DevicePin { DeviceId = deviceId, Name = name, Function = function, TechnicalData = technicalData });
        PlacementsChanged?.Invoke();
        return pinId;
    }

    public void UpdateDevicePin(DevicePin pin)
    {
        _devices.UpdateDevicePin(pin);
        PlacementsChanged?.Invoke();
    }

    public bool CanDeleteDevicePin(long devicePinId) => _connections.GetConnectionsForDevicePin(devicePinId).Count == 0;

    public void DeleteDevicePin(long devicePinId)
    {
        if (!CanDeleteDevicePin(devicePinId))
            throw new InvalidOperationException($"DevicePin {devicePinId} is still wired to a Connection.");

        _devices.DeleteDevicePin(devicePinId);
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
        var (result, anyConnectionsDeleted) = DeletePlacementCore(placementId);
        PlacementsChanged?.Invoke();
        if (anyConnectionsDeleted) ConnectionsChanged?.Invoke();
        return result;
    }

    /// <summary>
    /// ADR-015: deletes a Device and every Placement it has across every page (and their dependent
    /// Connections), in one action — the Devices grid's "delete" means delete the device wherever
    /// it's drawn, not leave orphaned symbols behind. Devices (and Cables) are parts with independent
    /// identity, so removing one is a real, if drastic, action the confirmation dialog gates;
    /// contrast Connections, which are a derived fact of two placements' geometry and have no
    /// grid-delete at all (the Connections tab's Delete Selected was removed, not just guarded).
    /// Loops DeletePlacementCore (not the public DeletePlacement) so PlacementsChanged/
    /// ConnectionsChanged each fire at most once for the whole batch, not once per placement.
    /// </summary>
    public void DeleteDeviceCascade(long deviceId)
    {
        var placementIds = _placements.GetPlacementIdsForDevice(deviceId);
        var anyConnectionsDeleted = false;

        if (placementIds.Count == 0)
        {
            _devices.DeleteDevice(deviceId);
        }
        else
        {
            foreach (var placementId in placementIds)
            {
                var (_, connectionsDeletedForThisPlacement) = DeletePlacementCore(placementId);
                anyConnectionsDeleted |= connectionsDeletedForThisPlacement;
            }
        }

        PlacementsChanged?.Invoke();
        if (anyConnectionsDeleted) ConnectionsChanged?.Invoke();
    }

    private (PlacementDeletionResult Result, bool AnyConnectionsDeleted) DeletePlacementCore(long placementId)
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

        var result = new PlacementDeletionResult(deviceDeleted, device.Id, device.DeviceTagSegment, device.FunctionSegment, device.LocationSegment, pinNames);
        return (result, anyConnectionsDeleted);
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
    /// Creates a Connection between two DevicePins (Section 5.5) with both ConnectionEnds
    /// (terminations off by default — Phase 1's "optional, toggleable per end"). Used for both manual
    /// wire-drawing and auto-connect (Section 6.1: "touching a symbol pin creates a Connection"); the
    /// caller resolves whether two pins are actually touching (a pure, symbol-geometry-aware concern —
    /// see Ecad.Rendering.Canvas.AutoConnectDetector) before calling this. No wire number or
    /// definition point is assigned here — a wire shows nothing until the user explicitly places a
    /// DefinitionPoint and (optionally) attaches it (see PlaceDefinitionPoint/AttachDefinitionPointToConnection).
    /// </summary>
    public Connection CreateConnection(long fromDevicePinId, long toDevicePinId)
    {
        var connection = new Connection
        {
            FromDevicePinId = fromDevicePinId,
            ToDevicePinId = toDevicePinId,
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

    /// <summary>M8: every Connection in the project regardless of page — the Connections grid isn't
    /// limited to same-page wiring the way the canvas's GetConnectionsForPage is.</summary>
    public IReadOnlyList<Connection> GetAllConnections() => _connections.GetAllConnectionsForProject(CurrentProject.Id);

    public bool AreConnected(long devicePinIdA, long devicePinIdB) => _connections.AreConnected(devicePinIdA, devicePinIdB);

    public void UpdateConnectionColor(long connectionId, string? color)
    {
        _connections.UpdateConnectionColor(connectionId, color);
        ConnectionsChanged?.Invoke();
    }

    public void UpdateConnectionCrossSection(long connectionId, double? crossSectionMm2)
    {
        _connections.UpdateConnectionCrossSection(connectionId, crossSectionMm2);
        ConnectionsChanged?.Invoke();
    }

    public void UpdateConnectionCable(long connectionId, long? cableId, long? cableCoreId)
    {
        _connections.UpdateConnectionCable(connectionId, cableId, cableCoreId);
        ConnectionsChanged?.Invoke();
    }

    public void UpdateConnectionEndpoints(long connectionId, long fromDevicePinId, long toDevicePinId)
    {
        _connections.UpdateConnectionEndpoints(connectionId, fromDevicePinId, toDevicePinId);
        ConnectionsChanged?.Invoke();
    }

    /// <summary>M8 bulk edit (Section 6.2's own example): sets one color on several Connections at
    /// once, raising ConnectionsChanged once for the whole batch rather than once per row.</summary>
    public void BulkUpdateConnectionColor(IReadOnlyList<long> connectionIds, string? color)
    {
        foreach (var connectionId in connectionIds)
            _connections.UpdateConnectionColor(connectionId, color);
        ConnectionsChanged?.Invoke();
    }

    public void BulkUpdateConnectionCrossSection(IReadOnlyList<long> connectionIds, double? crossSectionMm2)
    {
        foreach (var connectionId in connectionIds)
            _connections.UpdateConnectionCrossSection(connectionId, crossSectionMm2);
        ConnectionsChanged?.Invoke();
    }

    /// <summary>M9: every ConnectionEnd in the project, joined with parent-Connection context —
    /// the Terminations tab's one read query (Section 6.3's filterable view).</summary>
    public IReadOnlyList<ConnectionEndWithContext> GetAllConnectionEndsWithContext() =>
        _connections.GetAllConnectionEndsWithContext(CurrentProject.Id);

    /// <summary>M9: the Terminations tab's per-row inline edit — ConnectionEnd rows are never
    /// independently created or deleted (always exactly two per Connection, via CreateConnection),
    /// so this is the tab's only mutator besides the bulk-assign below. terminationPartId must
    /// already be a PROJECT-LOCAL Part.Id (EnsurePartCached's return value), same FK requirement as
    /// UpdateDevicePart.</summary>
    public void UpdateConnectionEndTermination(long connectionEndId, bool terminationEnabled,
        TerminationType terminationType, long? terminationPartId, double? strippingLengthMm)
    {
        _connections.UpdateConnectionEndTermination(connectionEndId, terminationEnabled, terminationType, terminationPartId, strippingLengthMm);
        ConnectionsChanged?.Invoke();
    }

    /// <summary>M9 bulk-assign (Section 6.3's own example): assigns one termination Part to several
    /// ConnectionEnds at once, raising ConnectionsChanged once for the whole batch — same convention
    /// as BulkUpdateDevicePart/BulkUpdateConnectionColor. terminationPartId must already be
    /// project-local — see EnsurePartCached (reused as-is, it only ever touches Part/Organization).</summary>
    public void BulkUpdateConnectionEndPart(IReadOnlyList<long> connectionEndIds, long? terminationPartId)
    {
        foreach (var connectionEndId in connectionEndIds)
            _connections.UpdateConnectionEndPart(connectionEndId, terminationPartId);
        ConnectionsChanged?.Invoke();
    }

    /// <summary>
    /// Places a definition point — an independent, symbol-like canvas entity carrying a wire's
    /// number/color/cross-section (see DefinitionPoint.cs). Unlike the connection itself (a derived
    /// fact of two placements' geometry with no independent identity, ADR-015), a definition point has
    /// its own absolute position and survives its attached connection being deleted/recreated (auto-
    /// connect's delete+recreate cycle on nearly every symbol move no longer wipes it). connectionId is
    /// optional: a definition point can be dropped in empty space with no wire underneath, or snapped
    /// onto one, in which case its fields are mirrored onto that Connection's own WireNumber/Color/
    /// CrossSectionMm2 columns so Grid Editor/Terminations (which read those columns directly) reflect
    /// it live.
    /// </summary>
    public DefinitionPoint PlaceDefinitionPoint(long pageId, double x, double y, string? wireNumber, string? color, double? crossSectionMm2, long? connectionId)
    {
        var point = new DefinitionPoint { PageId = pageId, X = x, Y = y, WireNumber = wireNumber, Color = color, CrossSectionMm2 = crossSectionMm2, ConnectionId = connectionId };
        point.Id = _definitionPoints.Insert(point);
        if (connectionId is { } id) MirrorDefinitionPointOntoConnection(id, wireNumber, color, crossSectionMm2);
        DefinitionPointsChanged?.Invoke();
        return point;
    }

    public void MoveDefinitionPoint(long definitionPointId, double x, double y)
    {
        _definitionPoints.UpdatePosition(definitionPointId, x, y);
        DefinitionPointsChanged?.Invoke();
    }

    /// <summary>Rotates a definition point's tick 90° at a time (R key) — purely cosmetic, never
    /// affects its position or attachment.</summary>
    public void RotateDefinitionPoint(long definitionPointId, int rotationDegrees)
    {
        _definitionPoints.UpdateRotation(definitionPointId, rotationDegrees);
        DefinitionPointsChanged?.Invoke();
    }

    /// <summary>Edits a definition point's wire number/color/cross-section, mirrored onto its attached
    /// connection's own columns if it has one (see PlaceDefinitionPoint). Callers are expected to have
    /// already validated wire-number uniqueness via IsWireNumberAvailable (same convention this app
    /// already uses for RenameDeviceTag) — this method does not re-validate.</summary>
    public void SetDefinitionPointData(long definitionPointId, string? wireNumber, string? color, double? crossSectionMm2)
    {
        _definitionPoints.UpdateData(definitionPointId, wireNumber, color, crossSectionMm2);
        var point = _definitionPoints.Get(definitionPointId);
        if (point?.ConnectionId is { } connectionId) MirrorDefinitionPointOntoConnection(connectionId, wireNumber, color, crossSectionMm2);
        DefinitionPointsChanged?.Invoke();
    }

    /// <summary>Attaches a definition point to a connection (dragging its tick onto a wire), mirroring
    /// its current data onto that connection's columns. Callers are expected to have already checked
    /// the target doesn't already have a different definition point attached — silently overwriting
    /// another wire's data would be a surprising way to lose it.</summary>
    public void AttachDefinitionPointToConnection(long definitionPointId, long connectionId)
    {
        var point = _definitionPoints.Get(definitionPointId) ?? throw new InvalidOperationException($"DefinitionPoint {definitionPointId} not found.");
        _definitionPoints.SetConnection(definitionPointId, connectionId);
        MirrorDefinitionPointOntoConnection(connectionId, point.WireNumber, point.Color, point.CrossSectionMm2);
        DefinitionPointsChanged?.Invoke();
    }

    /// <summary>Detaches a definition point from its connection (dragging its tick into empty space,
    /// or onto a wire that already has one), clearing the connection's mirrored fields back to blank —
    /// the point itself survives, unattached, exactly where it was left.</summary>
    public void DetachDefinitionPoint(long definitionPointId)
    {
        var point = _definitionPoints.Get(definitionPointId);
        if (point?.ConnectionId is { } connectionId) ClearDefinitionPointMirror(connectionId);
        _definitionPoints.SetConnection(definitionPointId, null);
        DefinitionPointsChanged?.Invoke();
    }

    /// <summary>Deletes a definition point outright — the only way one is ever removed now that it has
    /// independent identity (clearing its data in place, the shipped-then-reworked version's approach,
    /// no longer applies).</summary>
    public void DeleteDefinitionPoint(long definitionPointId)
    {
        var point = _definitionPoints.Get(definitionPointId);
        if (point?.ConnectionId is { } connectionId) ClearDefinitionPointMirror(connectionId);
        _definitionPoints.Delete(definitionPointId);
        DefinitionPointsChanged?.Invoke();
    }

    public IReadOnlyList<DefinitionPoint> GetDefinitionPoints(long pageId) => _definitionPoints.GetForPage(pageId);

    /// <summary>Every ConnectionId with an attached definition point, project-wide — the Grid Editor's
    /// Connections tab uses this to make Color/Cross-section read-only for those rows (the same "set
    /// via canvas" treatment WireNumber already gets).</summary>
    public IReadOnlyList<long> GetConnectionIdsWithDefinitionPoint() => _definitionPoints.GetAttachedConnectionIds(CurrentProject.Id);

    private void MirrorDefinitionPointOntoConnection(long connectionId, string? wireNumber, string? color, double? crossSectionMm2)
    {
        _connections.UpdateWireNumber(connectionId, wireNumber);
        _connections.UpdateConnectionColor(connectionId, color);
        _connections.UpdateConnectionCrossSection(connectionId, crossSectionMm2);
        ConnectionsChanged?.Invoke();
    }

    private void ClearDefinitionPointMirror(long connectionId) => MirrorDefinitionPointOntoConnection(connectionId, null, null, null);

    public bool IsWireNumberAvailable(string wireNumber, long? excludingDefinitionPointId) =>
        _definitionPoints.FindByWireNumber(CurrentProject.Id, wireNumber, excludingDefinitionPointId) is null;

    /// <summary>Simple sequential suggestion, project-wide (max existing integer + 1) — same
    /// simplification precedent as DeviceRepository.SuggestNextDesignation (ADR-008/ADR-009):
    /// not the "configurable per-page-or-project" scheme Section 6.1 mentions, just a starting
    /// point the user can freely overwrite.</summary>
    public string SuggestNextWireNumber()
    {
        var maxNumber = 0;
        foreach (var wireNumber in _definitionPoints.GetAllWireNumbers(CurrentProject.Id))
        {
            var match = Regex.Match(wireNumber, @"(\d+)$");
            if (match.Success && int.Parse(match.Value) > maxNumber) maxNumber = int.Parse(match.Value);
        }
        return (maxNumber + 1).ToString();
    }

    /// <summary>Reassigns every DefinitionPoint in the project fresh sequential wire numbers, ordered
    /// by page SortOrder then its own (Y, X) position (Section 6.1: "renumber command available"),
    /// mirroring each onto its attached connection if any. Returns the old-&gt;new number mapping so
    /// the caller can build an undoable command from it.</summary>
    public IReadOnlyList<(long DefinitionPointId, string? OldWireNumber, string NewWireNumber)> RenumberAllWires()
    {
        var definitionPointIds = _definitionPoints.GetIdsForRenumbering(CurrentProject.Id);
        var results = new List<(long, string?, string)>();

        var number = 1;
        foreach (var definitionPointId in definitionPointIds)
        {
            var point = _definitionPoints.Get(definitionPointId);
            var oldWireNumber = point?.WireNumber;
            var newWireNumber = number.ToString();
            _definitionPoints.UpdateData(definitionPointId, newWireNumber, point?.Color, point?.CrossSectionMm2);
            if (point?.ConnectionId is { } connectionId) _connections.UpdateWireNumber(connectionId, newWireNumber);
            results.Add((definitionPointId, oldWireNumber, newWireNumber));
            number++;
        }

        DefinitionPointsChanged?.Invoke();
        ConnectionsChanged?.Invoke();
        return results;
    }

    /// <summary>Reverts a RenumberAllWires() call using its returned mapping — for undo.</summary>
    public void ApplyWireNumbers(IReadOnlyList<(long DefinitionPointId, string? WireNumber)> assignments)
    {
        foreach (var (definitionPointId, wireNumber) in assignments)
        {
            var point = _definitionPoints.Get(definitionPointId);
            _definitionPoints.UpdateData(definitionPointId, wireNumber, point?.Color, point?.CrossSectionMm2);
            if (point?.ConnectionId is { } connectionId) _connections.UpdateWireNumber(connectionId, wireNumber);
        }
        DefinitionPointsChanged?.Invoke();
        ConnectionsChanged?.Invoke();
    }

    public Device? GetDevice(long deviceId) => _devices.GetDevice(deviceId);

    public IReadOnlyList<DevicePin> GetDevicePins(long deviceId) => _devices.GetDevicePins(deviceId);

    /// <summary>M12: every DevicePin in the project — report Builders resolve an arbitrary Connection's
    /// From/ToDevicePinId back to its owning Device without one query per connection.</summary>
    public IReadOnlyList<DevicePin> GetAllDevicePins() => _devices.GetAllDevicePins(CurrentProject.Id);

    public IReadOnlyList<PlacementPinInfo> GetPlacementPins(long placementId) => _placements.GetPlacementPins(placementId);

    public IReadOnlyList<Device> GetAllDevices() => _devices.GetAllDevices(CurrentProject.Id);

    public string SuggestNextDesignation(string? function, string? location) =>
        _devices.SuggestNextDesignation(CurrentProject.Id, function, location);

    public bool IsTagAvailable(string? function, string? location, string deviceTag, long? excludingDeviceId) =>
        _devices.FindByTag(CurrentProject.Id, function, location, deviceTag, excludingDeviceId) is null;

    public IReadOnlyList<Cable> GetAllCables() => _cables.GetAllCables(CurrentProject.Id);

    public Cable? GetCable(long cableId) => _cables.GetCable(cableId);

    public Cable CreateCable(Cable cable)
    {
        cable.ProjectId = CurrentProject.Id;
        cable.Id = _cables.InsertCable(cable);
        CablesChanged?.Invoke();
        return cable;
    }

    public void UpdateCable(Cable cable)
    {
        _cables.UpdateCable(cable);
        CablesChanged?.Invoke();
    }

    public bool CanDeleteCable(long cableId) => !_connections.AnyConnectionReferencesCable(cableId);

    /// <summary>Rejects (rather than auto-clearing) deleting a Cable still referenced by a Connection —
    /// losing a whole cable's identity silently would be a big, surprising loss. Contrast DeleteCableCore,
    /// which auto-clears instead of blocking, since a single core is a much smaller, easily-re-picked field.
    /// M12: also removes this cable's manufacturing-sheet report page, if one was ever generated — a
    /// report for a cable that no longer exists would otherwise linger, showing stale/orphaned data.</summary>
    public void DeleteCable(long cableId)
    {
        if (!CanDeleteCable(cableId))
            throw new InvalidOperationException($"Cable {cableId} is still referenced by a Connection.");

        var report = _generatedReports.GetByIdentity(ReportKinds.CableManufacturingSheet, cableId, null);
        if (report is not null)
        {
            _projects.DeletePage(report.PageId);
            _pages.RemoveAll(p => p.Id == report.PageId);
        }

        _cables.DeleteCable(cableId);
        CablesChanged?.Invoke();
        if (report is not null) PagesChanged?.Invoke();
    }

    public IReadOnlyList<CableCore> GetCableCores(long cableId) => _cables.GetCableCores(cableId);

    public CableCore AddCableCore(long cableId, CableCore core)
    {
        core.CableId = cableId;
        core.Id = _cables.InsertCableCore(core);
        CablesChanged?.Invoke();
        return core;
    }

    public void UpdateCableCore(CableCore core)
    {
        _cables.UpdateCableCore(core);
        CablesChanged?.Invoke();
    }

    /// <summary>Auto-clears CableCoreId (leaving CableId intact) on any Connection assigned to this
    /// core rather than blocking the delete — see DeleteCable for why the two guards differ.</summary>
    public void DeleteCableCore(long cableCoreId)
    {
        _connections.ClearCableCoreReferences(cableCoreId);
        _cables.DeleteCableCore(cableCoreId);
        CablesChanged?.Invoke();
        ConnectionsChanged?.Invoke();
    }

    /// <summary>What drawing/moving/re-homing a CableLine actually did — which crossed connections got
    /// newly assigned a core vs. which were skipped because they already belonged to a different cable
    /// (never silently overwritten). The caller (ViewModel) uses this to build a status message.</summary>
    public sealed record CableLineDrawResult(long CableLineId, IReadOnlyList<long> AssignedConnectionIds, IReadOnlyList<long> SkippedConnectionIds);

    /// <summary>
    /// Draws a new cable definition line: finds-or-creates a Cable by its (trimmed, case-insensitive)
    /// Tag — no pre-existing Cable or core setup required — then, for each connection the line crosses,
    /// auto-creates a sequentially-numbered CableCore (no Color/CrossSectionMm2 yet, filled in later via
    /// the Grid Editor) and mirrors the assignment onto Connection.CableId/CableCoreId via the existing
    /// UpdateConnectionCable. A crossed connection already assigned to a DIFFERENT cable is skipped, not
    /// overwritten.
    /// </summary>
    public CableLineDrawResult DrawCableLine(long pageId, double x1, double y1, double x2, double y2, string cableTag, IReadOnlyList<long> crossedConnectionIds)
    {
        var cable = FindOrCreateCableByTag(cableTag);
        var line = new CableLine { PageId = pageId, X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, CableId = cable.Id };
        line.Id = _cableLines.InsertCableLine(line);

        var (assigned, skipped) = AssignCrossings(line.Id, cable.Id, crossedConnectionIds);

        CableLinesChanged?.Invoke();
        if (assigned.Count > 0) ConnectionsChanged?.Invoke();
        return new CableLineDrawResult(line.Id, assigned, skipped);
    }

    /// <summary>Moves an existing cable line's geometry, then re-detects crossings at its new position
    /// (same cable, adding any newly-crossed connections — existing ones are left alone).</summary>
    public CableLineDrawResult MoveCableLine(long cableLineId, double x1, double y1, double x2, double y2, string cableTag, IReadOnlyList<long> crossedConnectionIds)
    {
        _cableLines.UpdateGeometry(cableLineId, x1, y1, x2, y2);
        return ReassignCableLine(cableLineId, cableTag, crossedConnectionIds);
    }

    /// <summary>
    /// Re-detects a cable line's crossings at its current position (used after a drag, or from the edit
    /// dialog). If cableTag resolves to a DIFFERENT cable than the line currently belongs to, every live
    /// crossing is first cleared from the old cable (mirror unassigned, crossing rows deleted) and the
    /// line is re-homed before assigning fresh crossings under the new cable — same "clear before
    /// reassign" shape DeleteDefinitionPointCommand already uses. If cableTag resolves to the SAME
    /// cable, existing crossings are left untouched and only newly-detected ones are added.
    /// </summary>
    public CableLineDrawResult ReassignCableLine(long cableLineId, string cableTag, IReadOnlyList<long> crossedConnectionIds)
    {
        var line = _cableLines.GetCableLine(cableLineId) ?? throw new InvalidOperationException($"CableLine {cableLineId} not found.");
        var cable = FindOrCreateCableByTag(cableTag);

        if (cable.Id != line.CableId)
        {
            foreach (var crossing in _cableLines.GetCrossingsForLine(cableLineId))
            {
                if (crossing.ConnectionId is { } connectionId)
                    _connections.UpdateConnectionCable(connectionId, null, null);
            }
            _cableLines.DeleteCrossingsForLine(cableLineId);
            _cableLines.UpdateCableId(cableLineId, cable.Id);
        }

        var (assigned, skipped) = AssignCrossings(cableLineId, cable.Id, crossedConnectionIds);

        CableLinesChanged?.Invoke();
        ConnectionsChanged?.Invoke();
        return new CableLineDrawResult(cableLineId, assigned, skipped);
    }

    /// <summary>Deletes a cable line entirely — clears every live crossing's mirrored Connection.CableId/
    /// CableCoreId first (the crossing rows themselves cascade with the line). The Cable and any
    /// CableCore rows it created are left alone (cheap, easily-re-picked, same ADR-010 precedent).</summary>
    public void DeleteCableLine(long cableLineId)
    {
        var anyLiveCrossing = false;
        foreach (var crossing in _cableLines.GetCrossingsForLine(cableLineId))
        {
            if (crossing.ConnectionId is not { } connectionId) continue;
            _connections.UpdateConnectionCable(connectionId, null, null);
            anyLiveCrossing = true;
        }

        _cableLines.DeleteCableLine(cableLineId);
        CableLinesChanged?.Invoke();
        if (anyLiveCrossing) ConnectionsChanged?.Invoke();
    }

    public IReadOnlyList<CableLine> GetCableLines(long pageId) => _cableLines.GetCableLinesForPage(pageId);

    public CableLine? GetCableLine(long cableLineId) => _cableLines.GetCableLine(cableLineId);

    public IReadOnlyList<CableLineCrossing> GetCableLineCrossings(long cableLineId) => _cableLines.GetCrossingsForLine(cableLineId);

    public CableLineCrossing? GetCableLineCrossing(long crossingId) => _cableLines.GetCrossing(crossingId);

    /// <summary>Rotates a cable line crossing's tick 90° at a time (R key) — purely cosmetic, never
    /// affects which wire/core it represents.</summary>
    public void RotateCableLineCrossing(long crossingId, int rotationDegrees)
    {
        _cableLines.UpdateCrossingRotation(crossingId, rotationDegrees);
        CableLinesChanged?.Invoke();
    }

    /// <summary>Edits a single crossing's own CableCore — number, color, cross-section — mirroring
    /// color/cross-section onto its connection if still live. Callers are expected to have already
    /// validated core-number uniqueness via IsCableCoreNumberAvailable, same convention as
    /// IsWireNumberAvailable.</summary>
    public void SetCableLineCrossingCore(long crossingId, int coreNumber, string? color, double? crossSectionMm2)
    {
        var crossing = _cableLines.GetCrossing(crossingId) ?? throw new InvalidOperationException($"CableLineCrossing {crossingId} not found.");
        var core = _cables.GetCableCore(crossing.CableCoreId) ?? throw new InvalidOperationException($"CableCore {crossing.CableCoreId} not found.");
        core.CoreNumber = coreNumber;
        core.Color = color;
        core.CrossSectionMm2 = crossSectionMm2;
        _cables.UpdateCableCore(core);

        if (crossing.ConnectionId is { } connectionId)
        {
            _connections.UpdateConnectionColor(connectionId, color);
            _connections.UpdateConnectionCrossSection(connectionId, crossSectionMm2);
        }

        CableLinesChanged?.Invoke();
        if (crossing.ConnectionId is not null) ConnectionsChanged?.Invoke();
    }

    public bool IsCableCoreNumberAvailable(long cableId, int coreNumber, long? excludingCoreId) =>
        !_cables.GetCableCores(cableId).Any(c => c.CoreNumber == coreNumber && c.Id != excludingCoreId);

    /// <summary>Every ConnectionId currently crossed (live) by a CableLine, project-wide — the Grid
    /// Editor's Connections tab uses this to make Cable/CableCore read-only for those rows.</summary>
    public IReadOnlyList<long> GetConnectionIdsWithCableLineCrossing() => _cableLines.GetAttachedConnectionIds(CurrentProject.Id);

    /// <summary>Simple sequential suggestion in the "-W12" style already used in Cable.cs's own example —
    /// same trailing-digit-increment convention as SuggestNextWireNumber, just scoped to Cable.Tag.</summary>
    public string SuggestNextCableTag()
    {
        var maxNumber = 0;
        foreach (var cable in _cables.GetAllCables(CurrentProject.Id))
        {
            var match = Regex.Match(cable.Tag, @"(\d+)$");
            if (match.Success && int.Parse(match.Value) > maxNumber) maxNumber = int.Parse(match.Value);
        }
        return $"-W{maxNumber + 1}";
    }

    /// <summary>What a report generation/regeneration call actually did — whether the Page was newly
    /// created or an existing one was reused (regenerated in place). Ecad.Reports.LayoutSchema owns the
    /// authoritative ReportKind string constants; these plain-string duplicates exist because Ecad.Data
    /// must not reference Ecad.Reports (STRUCTURE.md's dependency direction) — keep both in sync by hand.</summary>
    public sealed record ReportPageResult(Page Page, bool WasNewlyCreated);

    private static class ReportKinds
    {
        public const string CableManufacturingSheet = "CableManufacturingSheet";
    }

    /// <summary>
    /// Finds-or-creates the Page hosting a generated report, keyed by (reportKind, sourceEntityId,
    /// groupingKey) — the same identity GeneratedReport's unique index enforces. An existing match is
    /// reused in place (its GeneratedAtUtc bumped) so re-running a report never creates a duplicate page
    /// or shifts numbering (Section 6.4: "updates existing generated pages without page-number
    /// collisions"); a miss creates a new Page under documentTypeSegment with the next PageNumberSegment
    /// within that segment. sourceEntityId is a Cable.Id for a manufacturing-sheet page, else null;
    /// groupingKey discriminates BOM grouping mode, else null.
    /// </summary>
    public ReportPageResult UpsertGeneratedReportPage(string reportKind, string documentTypeSegment, long? sourceEntityId, string? groupingKey)
    {
        var existingReport = _generatedReports.GetByIdentity(reportKind, sourceEntityId, groupingKey);
        if (existingReport is not null)
        {
            _generatedReports.UpdateGeneratedAt(existingReport.Id, DateTimeOffset.UtcNow);
            var existingPage = _pages.Single(p => p.Id == existingReport.PageId);
            PagesChanged?.Invoke();
            return new ReportPageResult(existingPage, false);
        }

        var pageNumber = _pages.Count(p => p.DocumentTypeSegment == documentTypeSegment) + 1;
        var page = AddPage(new Page
        {
            DocumentTypeSegment = documentTypeSegment,
            PageNumberSegment = pageNumber.ToString(),
            PageType = PageType.Report,
            SortOrder = _projects.GetMaxSortOrder(CurrentProject.Id) + 1,
        });

        var report = new GeneratedReport
        {
            PageId = page.Id,
            ReportKind = reportKind,
            SourceEntityId = sourceEntityId,
            GroupingKey = groupingKey,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
        };
        report.Id = _generatedReports.Insert(report);

        PagesChanged?.Invoke();
        return new ReportPageResult(page, true);
    }

    /// <summary>The GeneratedReport identity behind a Page, if it is a report page — a ReportPageViewModel
    /// uses this on open/regenerate to know which report kind + source entity to re-render.</summary>
    public GeneratedReport? GetGeneratedReportForPage(long pageId) => _generatedReports.GetByPageId(pageId);

    /// <summary>Removes any manufacturing-sheet report page whose Cable no longer exists in
    /// stillLiveCableIds — the batch "Generate All Manufacturing Sheets" action's orphan cleanup, for a
    /// cable renamed/deleted since the last regeneration.</summary>
    public void DeleteOrphanedCableManufacturingSheets(IReadOnlyList<long> stillLiveCableIds)
    {
        var liveIds = stillLiveCableIds.ToHashSet();
        var anyDeleted = false;
        foreach (var report in _generatedReports.GetAllForKind(CurrentProject.Id, ReportKinds.CableManufacturingSheet))
        {
            if (report.SourceEntityId is { } cableId && !liveIds.Contains(cableId))
            {
                _projects.DeletePage(report.PageId);
                _pages.RemoveAll(p => p.Id == report.PageId);
                anyDeleted = true;
            }
        }
        if (anyDeleted) PagesChanged?.Invoke();
    }

    /// <summary>Deletes a Page outright — the first general-purpose page deletion in the codebase (M12).
    /// GeneratedReport rows cascade with their Page.</summary>
    public void DeletePage(long pageId)
    {
        _projects.DeletePage(pageId);
        _pages.RemoveAll(p => p.Id == pageId);
        PagesChanged?.Invoke();
    }

    private Cable FindOrCreateCableByTag(string cableTag)
    {
        var trimmed = cableTag.Trim();
        var existing = _cables.GetAllCables(CurrentProject.Id)
            .FirstOrDefault(c => string.Equals(c.Tag, trimmed, StringComparison.OrdinalIgnoreCase));
        return existing ?? CreateCable(new Cable { Tag = trimmed });
    }

    /// <summary>Assigns each not-yet-crossed connection in crossedConnectionIds a fresh, sequentially-
    /// numbered CableCore under the given cable and mirrors it onto Connection.CableId/CableCoreId.
    /// Connections already crossed by this same line are left alone (idempotent re-detection); a
    /// connection already assigned to a DIFFERENT cable is skipped, never overwritten.</summary>
    private (List<long> Assigned, List<long> Skipped) AssignCrossings(long cableLineId, long cableId, IReadOnlyList<long> crossedConnectionIds)
    {
        var alreadyCrossedConnectionIds = _cableLines.GetCrossingsForLine(cableLineId)
            .Where(c => c.ConnectionId is not null)
            .Select(c => c.ConnectionId!.Value)
            .ToHashSet();
        var nextCoreNumber = 1;
        var existingCores = _cables.GetCableCores(cableId);
        if (existingCores.Count > 0) nextCoreNumber = existingCores.Max(c => c.CoreNumber) + 1;

        var assigned = new List<long>();
        var skipped = new List<long>();

        foreach (var connectionId in crossedConnectionIds)
        {
            if (alreadyCrossedConnectionIds.Contains(connectionId)) continue;

            var connection = _connections.GetConnection(connectionId);
            if (connection is null) continue;
            if (connection.CableId is { } existingCableId && existingCableId != cableId)
            {
                skipped.Add(connectionId);
                continue;
            }

            var core = new CableCore { CableId = cableId, CoreNumber = nextCoreNumber };
            core.Id = _cables.InsertCableCore(core);
            nextCoreNumber++;

            _cableLines.InsertCrossing(new CableLineCrossing { CableLineId = cableLineId, ConnectionId = connectionId, CableCoreId = core.Id });
            _connections.UpdateConnectionCable(connectionId, cableId, core.Id);
            assigned.Add(connectionId);
        }

        return (assigned, skipped);
    }

    public void Dispose() => _connection.Dispose();
}
