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
