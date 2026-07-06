using Ecad.App.ViewModels;
using Ecad.Data;
using Ecad.Rendering.Canvas;
using Ecad.Rendering.Symbols;

namespace Ecad.App.Canvas;

/// <summary>Places a new symbol on the page. Undo deletes the placement it created.</summary>
internal sealed class PlaceSymbolCommand : IUndoableCommand
{
    private readonly ProjectSession _session;
    private readonly SchematicPageViewModel _viewModel;
    private readonly long _pageId;
    private readonly LoadedSymbol _symbol;
    private readonly IReadOnlyList<string> _pinNames;
    private readonly double _x;
    private readonly double _y;
    private readonly string _deviceTag;
    private long _placementId;

    public PlaceSymbolCommand(ProjectSession session, SchematicPageViewModel viewModel, long pageId,
        LoadedSymbol symbol, IReadOnlyList<string> pinNames, double x, double y, string deviceTag)
    {
        _session = session;
        _viewModel = viewModel;
        _pageId = pageId;
        _symbol = symbol;
        _pinNames = pinNames;
        _x = x;
        _y = y;
        _deviceTag = deviceTag;
    }

    public void Do()
    {
        var placement = _session.PlaceSymbol(_pageId, _symbol.Definition.Name, "Starter", _symbol.SvgFilePath,
            _symbol.Definition.Category, _pinNames, _x, _y, _deviceTag);
        _placementId = placement.Id;
        _viewModel.AddPlacementToView(placement.Id, placement.DeviceId, _deviceTag, _symbol, _x, _y, 0, false);
    }

    public void Undo()
    {
        _session.DeletePlacement(_placementId);
        _viewModel.RemovePlacementFromView(_placementId);
    }
}

/// <summary>Renames a placement's device tag between two known values.</summary>
internal sealed class RenameTagCommand(ProjectSession session, SchematicPageViewModel viewModel, long placementId,
    long deviceId, string fromTag, string toTag) : IUndoableCommand
{
    public void Do()
    {
        session.RenameDevice(deviceId, toTag);
        viewModel.UpdatePlacementTag(placementId, toTag);
    }

    public void Undo()
    {
        session.RenameDevice(deviceId, fromTag);
        viewModel.UpdatePlacementTag(placementId, fromTag);
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
/// Deletes a placement. Undo re-creates a new Device/Placement with the same visual properties
/// rather than restoring the exact original row IDs — a deliberate simplification (see
/// DECISIONS.md): the M1 schema cascades a Device delete through DevicePin/Placement/PlacementPin,
/// so there's nothing left to "undelete" by ID, only enough information to recreate it visually.
/// </summary>
internal sealed class DeleteCommand : IUndoableCommand
{
    private readonly ProjectSession _session;
    private readonly SchematicPageViewModel _viewModel;
    private readonly long _pageId;
    private readonly LoadedSymbol _symbol;
    private readonly IReadOnlyList<string> _pinNames;
    private readonly string _deviceTag;
    private readonly double _x;
    private readonly double _y;
    private readonly int _rotationDegrees;
    private readonly bool _mirrored;
    private long _placementId;

    public DeleteCommand(ProjectSession session, SchematicPageViewModel viewModel, long pageId,
        PlacementViewItem snapshot, LoadedSymbol symbol, IReadOnlyList<string> pinNames)
    {
        _session = session;
        _viewModel = viewModel;
        _pageId = pageId;
        _symbol = symbol;
        _pinNames = pinNames;
        _placementId = snapshot.PlacementId;
        _deviceTag = snapshot.DeviceTag;
        _x = snapshot.X;
        _y = snapshot.Y;
        _rotationDegrees = snapshot.RotationDegrees;
        _mirrored = snapshot.Mirrored;
    }

    public void Do()
    {
        _session.DeletePlacement(_placementId);
        _viewModel.RemovePlacementFromView(_placementId);
    }

    public void Undo()
    {
        var placement = _session.PlaceSymbol(_pageId, _symbol.Definition.Name, "Starter", _symbol.SvgFilePath,
            _symbol.Definition.Category, _pinNames, _x, _y, _deviceTag);
        if (_rotationDegrees != 0 || _mirrored)
            _session.RotatePlacement(placement.Id, _rotationDegrees, _mirrored);

        _placementId = placement.Id;
        _viewModel.AddPlacementToView(placement.Id, placement.DeviceId, _deviceTag, _symbol, _x, _y, _rotationDegrees, _mirrored);
    }
}
