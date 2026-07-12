using Ecad.App.ViewModels;
using Ecad.Data;
using Ecad.Rendering.Canvas;
using Ecad.Rendering.Symbols;

namespace Ecad.App.Canvas;

/// <summary>Places a new symbol on the page as a brand-new Device. Undo deletes the placement it created.</summary>
internal sealed class PlaceSymbolCommand : IUndoableCommand
{
    private readonly ProjectSession _session;
    private readonly SchematicPageViewModel _viewModel;
    private readonly long _pageId;
    private readonly LoadedSymbol _symbol;
    private readonly IReadOnlyList<string> _pinNames;
    private readonly double _x;
    private readonly double _y;
    private readonly string? _function;
    private readonly string? _location;
    private readonly string _deviceTag;
    private long _placementId;

    /// <summary>The Placement this command created — read by the ViewModel right after Execute to run auto-connect (M7).</summary>
    public long PlacementId => _placementId;

    public PlaceSymbolCommand(ProjectSession session, SchematicPageViewModel viewModel, long pageId,
        LoadedSymbol symbol, IReadOnlyList<string> pinNames, double x, double y,
        string? function, string? location, string deviceTag)
    {
        _session = session;
        _viewModel = viewModel;
        _pageId = pageId;
        _symbol = symbol;
        _pinNames = pinNames;
        _x = x;
        _y = y;
        _function = function;
        _location = location;
        _deviceTag = deviceTag;
    }

    public void Do()
    {
        var placement = _session.PlaceSymbol(_pageId, _symbol.Definition.Name, "Starter", _symbol.SvgFilePath,
            _symbol.Definition.Category, _pinNames, _x, _y, _function, _location, _deviceTag);
        _placementId = placement.Id;
        _viewModel.AddPlacementToView(placement.Id, placement.DeviceId, _function, _location, _deviceTag, _symbol, _x, _y, 0, false);
    }

    public void Undo()
    {
        _session.DeletePlacement(_placementId);
        _viewModel.RemovePlacementFromView(_placementId);
    }
}

/// <summary>
/// Places a new symbol as another Placement of an EXISTING Device (M6: multi-placement devices).
/// Undo removes just this placement — the Device and its other placements are untouched, since
/// they were already there before this command ran.
/// </summary>
internal sealed class AttachPlacementCommand : IUndoableCommand
{
    private readonly ProjectSession _session;
    private readonly SchematicPageViewModel _viewModel;
    private readonly long _pageId;
    private readonly long _deviceId;
    private readonly string? _function;
    private readonly string? _location;
    private readonly string _deviceTag;
    private readonly LoadedSymbol _symbol;
    private readonly IReadOnlyList<string> _pinNames;
    private readonly double _x;
    private readonly double _y;
    private long _placementId;

    public long PlacementId => _placementId;

    public AttachPlacementCommand(ProjectSession session, SchematicPageViewModel viewModel, long pageId, long deviceId,
        string? function, string? location, string deviceTag, LoadedSymbol symbol, IReadOnlyList<string> pinNames, double x, double y)
    {
        _session = session;
        _viewModel = viewModel;
        _pageId = pageId;
        _deviceId = deviceId;
        _function = function;
        _location = location;
        _deviceTag = deviceTag;
        _symbol = symbol;
        _pinNames = pinNames;
        _x = x;
        _y = y;
    }

    public void Do()
    {
        var placement = _session.PlaceSymbolOnExistingDevice(_deviceId, _pageId, _symbol.Definition.Name, "Starter",
            _symbol.SvgFilePath, _symbol.Definition.Category, _pinNames, _x, _y);
        _placementId = placement.Id;
        _viewModel.AddPlacementToView(placement.Id, _deviceId, _function, _location, _deviceTag, _symbol, _x, _y, 0, false);
    }

    public void Undo()
    {
        _session.DeletePlacement(_placementId);
        _viewModel.RemovePlacementFromView(_placementId);
    }
}

/// <summary>Renames a placement's device tag (all three IEC 81346 segments) between two known states.</summary>
internal sealed class RenameTagCommand(ProjectSession session, SchematicPageViewModel viewModel, long placementId, long deviceId,
    string? fromFunction, string? fromLocation, string fromTag, string? toFunction, string? toLocation, string toTag) : IUndoableCommand
{
    public void Do()
    {
        session.RenameDeviceTag(deviceId, toFunction, toLocation, toTag);
        viewModel.UpdatePlacementTag(placementId, toFunction, toLocation, toTag);
    }

    public void Undo()
    {
        session.RenameDeviceTag(deviceId, fromFunction, fromLocation, fromTag);
        viewModel.UpdatePlacementTag(placementId, fromFunction, fromLocation, fromTag);
    }
}

/// <summary>Moves an existing placement between two known world positions.</summary>
internal sealed class MoveCommand(ProjectSession session, SchematicPageViewModel viewModel, long placementId,
    double fromX, double fromY, double toX, double toY) : IUndoableCommand
{
    public void Do()
    {
        session.MovePlacement(placementId, toX, toY);
        viewModel.UpdatePlacementPosition(placementId, toX, toY);
    }

    public void Undo()
    {
        session.MovePlacement(placementId, fromX, fromY);
        viewModel.UpdatePlacementPosition(placementId, fromX, fromY);
    }
}

/// <summary>Rotates (and/or mirrors) an existing placement between two known states.</summary>
internal sealed class RotateCommand(ProjectSession session, SchematicPageViewModel viewModel, long placementId,
    int fromRotation, bool fromMirrored, int toRotation, bool toMirrored) : IUndoableCommand
{
    public void Do()
    {
        session.RotatePlacement(placementId, toRotation, toMirrored);
        viewModel.UpdatePlacementRotation(placementId, toRotation, toMirrored);
    }

    public void Undo()
    {
        session.RotatePlacement(placementId, fromRotation, fromMirrored);
        viewModel.UpdatePlacementRotation(placementId, fromRotation, fromMirrored);
    }
}

/// <summary>
/// Deletes a placement. If it was the Device's last placement (see DECISIONS.md ADR-007/ADR-008),
/// undo re-creates a whole new Device with the same visual properties — new row IDs, visually
/// identical. If sibling placements remain (M6: multi-placement devices), the Device survives the
/// delete, so undo instead re-creates just a new Placement on that same, still-existing Device.
/// Either way there's nothing to "undelete" by ID once the DB call returns.
/// </summary>
internal sealed class DeleteCommand : IUndoableCommand
{
    private readonly ProjectSession _session;
    private readonly SchematicPageViewModel _viewModel;
    private readonly long _pageId;
    private readonly LoadedSymbol _symbol;
    private readonly double _x;
    private readonly double _y;
    private readonly int _rotationDegrees;
    private readonly bool _mirrored;
    private long _placementId;
    private PlacementDeletionResult? _deletionResult;

    public DeleteCommand(ProjectSession session, SchematicPageViewModel viewModel, long pageId, PlacementViewItem snapshot, LoadedSymbol symbol)
    {
        _session = session;
        _viewModel = viewModel;
        _pageId = pageId;
        _symbol = symbol;
        _placementId = snapshot.PlacementId;
        _x = snapshot.X;
        _y = snapshot.Y;
        _rotationDegrees = snapshot.RotationDegrees;
        _mirrored = snapshot.Mirrored;
    }

    public void Do()
    {
        _deletionResult = _session.DeletePlacement(_placementId);
        _viewModel.RemovePlacementFromView(_placementId);
    }

    public void Undo()
    {
        var result = _deletionResult ?? throw new InvalidOperationException("Undo called before Do.");

        var placement = result.DeviceDeleted
            ? _session.PlaceSymbol(_pageId, _symbol.Definition.Name, "Starter", _symbol.SvgFilePath,
                _symbol.Definition.Category, result.PinNames, _x, _y, result.Function, result.Location, result.DeviceTag)
            : _session.PlaceSymbolOnExistingDevice(result.DeviceId, _pageId, _symbol.Definition.Name, "Starter",
                _symbol.SvgFilePath, _symbol.Definition.Category, result.PinNames, _x, _y);

        if (_rotationDegrees != 0 || _mirrored)
            _session.RotatePlacement(placement.Id, _rotationDegrees, _mirrored);

        _placementId = placement.Id;
        _viewModel.AddPlacementToView(placement.Id, placement.DeviceId, result.Function, result.Location, result.DeviceTag,
            _symbol, _x, _y, _rotationDegrees, _mirrored);
    }
}

/// <summary>
/// Creates a wire between two DevicePins — from manual drawing or auto-connect (Section 5.5/6.1).
/// Undo deletes it; there's nothing else to restore since a Connection carries no cached geometry.
/// </summary>
internal sealed class CreateConnectionCommand(ProjectSession session, SchematicPageViewModel viewModel, long fromDevicePinId, long toDevicePinId) : IUndoableCommand
{
    private long _connectionId;

    public void Do()
    {
        var connection = session.CreateConnection(fromDevicePinId, toDevicePinId);
        _connectionId = connection.Id;
        viewModel.AddConnectionToView(connection.Id, fromDevicePinId, toDevicePinId);
    }

    public void Undo()
    {
        session.DeleteConnection(_connectionId);
        viewModel.RemoveConnectionFromView(_connectionId);
    }
}

/// <summary>
/// Deletes a wire. Undo recreates it (new row Id) rather than restoring the original — the same
/// recreate-not-restore simplification as DeleteCommand (ADR-007/ADR-009), since ConnectionEnd rows
/// cascade away and there's nothing left to restore by Id. A definition point attached to the deleted
/// wire survives independently (the database detaches it, ON DELETE SET NULL) rather than being
/// restored here — reattaching it to whatever wire ends up in its place is a manual drag, same as any
/// other attach; see DECISIONS.md.
/// </summary>
internal sealed class DeleteConnectionCommand : IUndoableCommand
{
    private readonly ProjectSession _session;
    private readonly SchematicPageViewModel _viewModel;
    private readonly long _fromDevicePinId;
    private readonly long _toDevicePinId;
    private long _connectionId;

    public DeleteConnectionCommand(ProjectSession session, SchematicPageViewModel viewModel, ConnectionViewItem snapshot)
    {
        _session = session;
        _viewModel = viewModel;
        _connectionId = snapshot.ConnectionId;
        _fromDevicePinId = snapshot.FromDevicePinId;
        _toDevicePinId = snapshot.ToDevicePinId;
    }

    public void Do()
    {
        _session.DeleteConnection(_connectionId);
        _viewModel.RemoveConnectionFromView(_connectionId);
        _viewModel.DetachDefinitionPointsFromConnectionView(_connectionId);
    }

    public void Undo()
    {
        var connection = _session.CreateConnection(_fromDevicePinId, _toDevicePinId);
        _connectionId = connection.Id;
        _viewModel.AddConnectionToView(connection.Id, _fromDevicePinId, _toDevicePinId);
    }
}

/// <summary>Places a new definition point. Undo deletes it — the same recreate-not-restore shape as
/// PlaceSymbolCommand/CreateConnectionCommand (a fresh Do() after undo gets a new row Id).</summary>
internal sealed class PlaceDefinitionPointCommand(ProjectSession session, SchematicPageViewModel viewModel, long pageId,
    double x, double y, string? wireNumber, string? color, double? crossSectionMm2, long? connectionId) : IUndoableCommand
{
    private long _definitionPointId;

    public void Do()
    {
        var point = session.PlaceDefinitionPoint(pageId, x, y, wireNumber, color, crossSectionMm2, connectionId);
        _definitionPointId = point.Id;
        viewModel.AddDefinitionPointToView(point.Id, point.X, point.Y, point.WireNumber, point.Color, point.CrossSectionMm2, point.ConnectionId);
    }

    public void Undo()
    {
        session.DeleteDefinitionPoint(_definitionPointId);
        viewModel.RemoveDefinitionPointFromView(_definitionPointId);
    }
}

/// <summary>
/// Moves an existing definition point between two known world positions, optionally also changing
/// which connection (if any) it's attached to — one drag can do both at once (drop it on a different
/// wire, or into empty space). Mirrors MoveCommand's plain before/after shape, just two fields wider.
/// </summary>
internal sealed class MoveDefinitionPointCommand(ProjectSession session, SchematicPageViewModel viewModel, long definitionPointId,
    double fromX, double fromY, long? fromConnectionId, double toX, double toY, long? toConnectionId) : IUndoableCommand
{
    public void Do() => Apply(toX, toY, fromConnectionId, toConnectionId);
    public void Undo() => Apply(fromX, fromY, toConnectionId, fromConnectionId);

    private void Apply(double x, double y, long? currentConnectionId, long? nextConnectionId)
    {
        session.MoveDefinitionPoint(definitionPointId, x, y);
        if (currentConnectionId != nextConnectionId)
        {
            if (currentConnectionId is not null) session.DetachDefinitionPoint(definitionPointId);
            if (nextConnectionId is { } id) session.AttachDefinitionPointToConnection(definitionPointId, id);
        }
        viewModel.UpdateDefinitionPointPosition(definitionPointId, x, y, nextConnectionId);
    }
}

/// <summary>Edits a definition point's wire number/color/cross-section between two known states — one
/// command shape for both "placing data on a fresh point" and "editing an existing one" (matching
/// MoveCommand/RotateCommand's single before/after pair). Does not touch position or attachment — see
/// MoveDefinitionPointCommand for that.</summary>
internal sealed class SetDefinitionPointDataCommand(ProjectSession session, SchematicPageViewModel viewModel, long definitionPointId,
    string? fromWireNumber, string? fromColor, double? fromCrossSectionMm2,
    string? toWireNumber, string? toColor, double? toCrossSectionMm2) : IUndoableCommand
{
    public void Do() => Apply(toWireNumber, toColor, toCrossSectionMm2);
    public void Undo() => Apply(fromWireNumber, fromColor, fromCrossSectionMm2);

    private void Apply(string? wireNumber, string? color, double? crossSectionMm2)
    {
        session.SetDefinitionPointData(definitionPointId, wireNumber, color, crossSectionMm2);
        viewModel.UpdateDefinitionPointData(definitionPointId, wireNumber, color, crossSectionMm2);
    }
}

/// <summary>Rotates a definition point's tick between two known 90°-step states (R key) — purely
/// cosmetic, same before/after-pair shape as RotateCommand.</summary>
internal sealed class RotateDefinitionPointCommand(ProjectSession session, SchematicPageViewModel viewModel, long definitionPointId,
    int fromRotationDegrees, int toRotationDegrees) : IUndoableCommand
{
    public void Do() => Apply(toRotationDegrees);
    public void Undo() => Apply(fromRotationDegrees);

    private void Apply(int rotationDegrees)
    {
        session.RotateDefinitionPoint(definitionPointId, rotationDegrees);
        viewModel.UpdateDefinitionPointRotation(definitionPointId, rotationDegrees);
    }
}

/// <summary>
/// Deletes a definition point — the only way one is ever removed (dragging its tick around, or
/// detaching it from a wire, never destroys it; see DECISIONS.md). Undo recreates it (new row Id)
/// rather than restoring the original, the same recreate-not-restore shape as DeleteCommand.
/// </summary>
internal sealed class DeleteDefinitionPointCommand : IUndoableCommand
{
    private readonly ProjectSession _session;
    private readonly SchematicPageViewModel _viewModel;
    private readonly long _pageId;
    private readonly double _x;
    private readonly double _y;
    private readonly string? _wireNumber;
    private readonly string? _color;
    private readonly double? _crossSectionMm2;
    private readonly long? _connectionId;
    private readonly int _rotationDegrees;
    private long _definitionPointId;

    public DeleteDefinitionPointCommand(ProjectSession session, SchematicPageViewModel viewModel, long pageId, DefinitionPointViewItem snapshot)
    {
        _session = session;
        _viewModel = viewModel;
        _pageId = pageId;
        _definitionPointId = snapshot.Id;
        _x = snapshot.X;
        _y = snapshot.Y;
        _wireNumber = snapshot.WireNumber;
        _color = snapshot.Color;
        _crossSectionMm2 = snapshot.CrossSectionMm2;
        _connectionId = snapshot.ConnectionId;
        _rotationDegrees = snapshot.RotationDegrees;
    }

    public void Do()
    {
        _session.DeleteDefinitionPoint(_definitionPointId);
        _viewModel.RemoveDefinitionPointFromView(_definitionPointId);
    }

    public void Undo()
    {
        var point = _session.PlaceDefinitionPoint(_pageId, _x, _y, _wireNumber, _color, _crossSectionMm2, _connectionId);
        _definitionPointId = point.Id;
        if (_rotationDegrees != 0) _session.RotateDefinitionPoint(point.Id, _rotationDegrees);
        _viewModel.AddDefinitionPointToView(point.Id, point.X, point.Y, point.WireNumber, point.Color, point.CrossSectionMm2, point.ConnectionId, _rotationDegrees);
    }
}

/// <summary>Reassigns every wire number in the project sequentially (Section 6.1: "renumber command
/// available"). Undo restores each definition point's previous number via ProjectSession.ApplyWireNumbers.</summary>
internal sealed class RenumberWiresCommand(ProjectSession session, SchematicPageViewModel viewModel) : IUndoableCommand
{
    private IReadOnlyList<(long DefinitionPointId, string? OldWireNumber, string NewWireNumber)> _result = [];

    public void Do()
    {
        _result = session.RenumberAllWires();
        viewModel.ReloadConnectionsFromSession();
        viewModel.ReloadDefinitionPointsFromSession();
    }

    public void Undo()
    {
        session.ApplyWireNumbers(_result.Select(r => (r.DefinitionPointId, r.OldWireNumber)).ToList());
        viewModel.ReloadConnectionsFromSession();
        viewModel.ReloadDefinitionPointsFromSession();
    }
}

/// <summary>Draws a new cable definition line. Undo deletes it, clearing any mirrored Connection.CableId/
/// CableCoreId assignments it made — same recreate-not-restore shape as PlaceDefinitionPointCommand.</summary>
internal sealed class DrawCableLineCommand(ProjectSession session, SchematicPageViewModel viewModel, long pageId,
    double x1, double y1, double x2, double y2, string cableTag, IReadOnlyList<long> crossedConnectionIds) : IUndoableCommand
{
    private long _cableLineId;

    public void Do()
    {
        var result = session.DrawCableLine(pageId, x1, y1, x2, y2, cableTag, crossedConnectionIds);
        _cableLineId = result.CableLineId;
        var line = session.GetCableLine(_cableLineId)!;
        var tag = session.GetCable(line.CableId)?.Tag ?? cableTag;
        viewModel.AddCableLineToView(line.Id, line.X1, line.Y1, line.X2, line.Y2, line.CableId, tag);
    }

    public void Undo()
    {
        session.DeleteCableLine(_cableLineId);
        viewModel.RemoveCableLineFromView(_cableLineId);
    }
}

/// <summary>
/// Moves an existing cable line between two known endpoint pairs, re-detecting crossings (same cable) at
/// each position — a newly-reached wire picks up a fresh core, matching MoveCableLine's own semantics.
/// Undo only reverts position/re-detection, not any core assignments a since-undone drag newly made
/// (same accepted limitation as the rest of this feature — detection is explicit, not perfectly
/// bidirectional; see DECISIONS.md).
/// </summary>
internal sealed class MoveCableLineCommand(ProjectSession session, SchematicPageViewModel viewModel, long cableLineId,
    double fromX1, double fromY1, double fromX2, double fromY2, IReadOnlyList<long> fromCrossedConnectionIds,
    double toX1, double toY1, double toX2, double toY2, IReadOnlyList<long> toCrossedConnectionIds,
    string cableTag) : IUndoableCommand
{
    public void Do() => Apply(toX1, toY1, toX2, toY2, toCrossedConnectionIds);
    public void Undo() => Apply(fromX1, fromY1, fromX2, fromY2, fromCrossedConnectionIds);

    private void Apply(double x1, double y1, double x2, double y2, IReadOnlyList<long> crossedConnectionIds)
    {
        session.MoveCableLine(cableLineId, x1, y1, x2, y2, cableTag, crossedConnectionIds);
        viewModel.UpdateCableLineGeometry(cableLineId, x1, y1, x2, y2);
    }
}

/// <summary>
/// Re-homes a cable line to a different cable (double-click, edited Cable Tag) — every currently-live
/// crossing is cleared from the old cable and reassigned fresh cores under the found-or-created new one.
/// </summary>
internal sealed class ReassignCableLineCommand(ProjectSession session, SchematicPageViewModel viewModel, long cableLineId,
    string fromCableTag, string toCableTag, IReadOnlyList<long> crossedConnectionIds) : IUndoableCommand
{
    public void Do() => Apply(toCableTag);
    public void Undo() => Apply(fromCableTag);

    private void Apply(string cableTag)
    {
        session.ReassignCableLine(cableLineId, cableTag, crossedConnectionIds);
        var line = session.GetCableLine(cableLineId)!;
        var tag = session.GetCable(line.CableId)?.Tag ?? cableTag;
        viewModel.UpdateCableLineCable(cableLineId, line.CableId, tag);
    }
}

/// <summary>
/// Deletes a cable line — clears every live crossing's mirrored Connection.CableId/CableCoreId first
/// (ProjectSession.DeleteCableLine). Undo recreates it (new row Id, crossings re-detected fresh at the
/// same geometry) rather than restoring the original, same recreate-not-restore shape as
/// DeleteDefinitionPointCommand.
/// </summary>
internal sealed class DeleteCableLineCommand : IUndoableCommand
{
    private readonly ProjectSession _session;
    private readonly SchematicPageViewModel _viewModel;
    private readonly long _pageId;
    private readonly double _x1;
    private readonly double _y1;
    private readonly double _x2;
    private readonly double _y2;
    private readonly string _cableTag;
    private long _cableLineId;

    public DeleteCableLineCommand(ProjectSession session, SchematicPageViewModel viewModel, long pageId, CableLineViewItem snapshot)
    {
        _session = session;
        _viewModel = viewModel;
        _pageId = pageId;
        _cableLineId = snapshot.Id;
        _x1 = snapshot.X1;
        _y1 = snapshot.Y1;
        _x2 = snapshot.X2;
        _y2 = snapshot.Y2;
        _cableTag = snapshot.CableTag;
    }

    public void Do()
    {
        _session.DeleteCableLine(_cableLineId);
        _viewModel.RemoveCableLineFromView(_cableLineId);
    }

    public void Undo()
    {
        var crossedConnectionIds = _viewModel.DetectCableLineCrossings(_x1, _y1, _x2, _y2);
        var result = _session.DrawCableLine(_pageId, _x1, _y1, _x2, _y2, _cableTag, crossedConnectionIds);
        _cableLineId = result.CableLineId;
        var line = _session.GetCableLine(_cableLineId)!;
        var tag = _session.GetCable(line.CableId)?.Tag ?? _cableTag;
        _viewModel.AddCableLineToView(line.Id, line.X1, line.Y1, line.X2, line.Y2, line.CableId, tag);
    }
}

/// <summary>Rotates a cable line crossing's tick between two known 90°-step states (R key) — purely
/// cosmetic, same shape as RotateDefinitionPointCommand.</summary>
internal sealed class RotateCableLineCrossingCommand(ProjectSession session, SchematicPageViewModel viewModel, long crossingId,
    int fromRotationDegrees, int toRotationDegrees) : IUndoableCommand
{
    public void Do() => Apply(toRotationDegrees);
    public void Undo() => Apply(fromRotationDegrees);

    private void Apply(int rotationDegrees)
    {
        session.RotateCableLineCrossing(crossingId, rotationDegrees);
        viewModel.UpdateCableLineCrossingRotation(crossingId, rotationDegrees);
    }
}

/// <summary>Edits a single cable line crossing's own core number/color/cross-section between two known
/// states — same before/after-pair shape as SetDefinitionPointDataCommand, just sourced from a
/// CableCore instead of a Connection.</summary>
internal sealed class SetCableLineCrossingCoreCommand(ProjectSession session, SchematicPageViewModel viewModel, long crossingId,
    int fromCoreNumber, string? fromColor, double? fromCrossSectionMm2,
    int toCoreNumber, string? toColor, double? toCrossSectionMm2) : IUndoableCommand
{
    public void Do() => Apply(toCoreNumber, toColor, toCrossSectionMm2);
    public void Undo() => Apply(fromCoreNumber, fromColor, fromCrossSectionMm2);

    private void Apply(int coreNumber, string? color, double? crossSectionMm2)
    {
        session.SetCableLineCrossingCore(crossingId, coreNumber, color, crossSectionMm2);
        viewModel.UpdateCableLineCrossingCore(crossingId, coreNumber, color, crossSectionMm2);
    }
}

/// <summary>Wraps several commands so a multi-select group action (move-together, delete-together)
/// undoes/redoes as one atomic step on the UndoRedoStack. Undo runs in reverse order in case any
/// wrapped command depends on state set up by an earlier one (e.g. DeleteCommand's device-recreation).</summary>
internal sealed class CompositeCommand(IReadOnlyList<IUndoableCommand> commands) : IUndoableCommand
{
    public void Do()
    {
        foreach (var command in commands) command.Do();
    }

    public void Undo()
    {
        for (var i = commands.Count - 1; i >= 0; i--) commands[i].Undo();
    }
}
