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
        viewModel.AddConnectionToView(connection.Id, connection.WireNumber, fromDevicePinId, toDevicePinId);
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
/// cascade away and there's nothing left to restore by Id. The recreated wire's number is set back
/// to what it was, even though a fresh CreateConnection would otherwise auto-assign a new one.
/// </summary>
internal sealed class DeleteConnectionCommand : IUndoableCommand
{
    private readonly ProjectSession _session;
    private readonly SchematicPageViewModel _viewModel;
    private readonly long _fromDevicePinId;
    private readonly long _toDevicePinId;
    private readonly string? _wireNumber;
    private long _connectionId;

    public DeleteConnectionCommand(ProjectSession session, SchematicPageViewModel viewModel, ConnectionViewItem snapshot)
    {
        _session = session;
        _viewModel = viewModel;
        _connectionId = snapshot.ConnectionId;
        _fromDevicePinId = snapshot.FromDevicePinId;
        _toDevicePinId = snapshot.ToDevicePinId;
        _wireNumber = snapshot.WireNumber;
    }

    public void Do()
    {
        _session.DeleteConnection(_connectionId);
        _viewModel.RemoveConnectionFromView(_connectionId);
    }

    public void Undo()
    {
        var connection = _session.CreateConnection(_fromDevicePinId, _toDevicePinId);
        _connectionId = connection.Id;
        var wireNumber = _wireNumber ?? connection.WireNumber;
        if (wireNumber != connection.WireNumber) _session.RenameWireNumber(connection.Id, wireNumber!);
        _viewModel.AddConnectionToView(connection.Id, wireNumber, _fromDevicePinId, _toDevicePinId);
    }
}

/// <summary>Renames a Connection's wire number between two known values.</summary>
internal sealed class RenameWireNumberCommand(ProjectSession session, SchematicPageViewModel viewModel, long connectionId, string fromWireNumber, string toWireNumber) : IUndoableCommand
{
    public void Do()
    {
        session.RenameWireNumber(connectionId, toWireNumber);
        viewModel.UpdateConnectionWireNumber(connectionId, toWireNumber);
    }

    public void Undo()
    {
        session.RenameWireNumber(connectionId, fromWireNumber);
        viewModel.UpdateConnectionWireNumber(connectionId, fromWireNumber);
    }
}

/// <summary>Reassigns every wire number in the project sequentially (Section 6.1: "renumber command
/// available"). Undo restores each connection's previous number via ProjectSession.ApplyWireNumbers.</summary>
internal sealed class RenumberWiresCommand(ProjectSession session, SchematicPageViewModel viewModel) : IUndoableCommand
{
    private IReadOnlyList<(long ConnectionId, string? OldWireNumber, string NewWireNumber)> _result = [];

    public void Do()
    {
        _result = session.RenumberAllWires();
        viewModel.ReloadConnectionsFromSession();
    }

    public void Undo()
    {
        session.ApplyWireNumbers(_result.Select(r => (r.ConnectionId, r.OldWireNumber)).ToList());
        viewModel.ReloadConnectionsFromSession();
    }
}
