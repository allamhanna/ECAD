using Ecad.Data;

namespace Ecad.App.ViewModels;

/// <summary>
/// Composition root for the M8 Grid Editor window: one instance per open project. Devices, Cables,
/// Connections, and (last) Terminations have each moved out to the sidebar's own navigators
/// (MainViewModel.DevicesNavigator/CablesNavigator/ConnectionsNavigator/TerminalsNavigator), so
/// nothing is duplicated here anymore. Deliberately kept as an empty shell rather than removed
/// outright — File > Grid Editor still opens it — per explicit user direction, in case something
/// else lands here later.
/// </summary>
public sealed class GridEditorViewModel : IDisposable
{
    public GridEditorViewModel(ProjectSession session)
    {
    }

    public void Dispose()
    {
    }
}
