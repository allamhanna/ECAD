using Ecad.Data;

namespace Ecad.App.ViewModels;

/// <summary>
/// Composition root for the M8 Grid Editor window: one instance per open project, hosting the
/// Connections/Terminations tab view-models (Devices/Cables both moved to the sidebar's own
/// navigators — MainViewModel.DevicesNavigator/CablesNavigator — so neither is duplicated here).
/// Subscribes to ProjectSession's live-sync events once and fans out to whichever tab(s) hold
/// derived data — same cross-window live-sync pattern PlacementsChanged/ConnectionsChanged already
/// established for SchematicPageWindow (ADR-008/009).
/// </summary>
public sealed class GridEditorViewModel : IDisposable
{
    private readonly ProjectSession _session;

    public ConnectionsGridViewModel ConnectionsTab { get; }
    public TerminationsGridViewModel TerminationsTab { get; }

    public GridEditorViewModel(ProjectSession session)
    {
        _session = session;
        ConnectionsTab = new ConnectionsGridViewModel(session);
        TerminationsTab = new TerminationsGridViewModel(session);

        _session.PlacementsChanged += OnPlacementsChanged;
        _session.ConnectionsChanged += OnConnectionsChanged;
        _session.CablesChanged += OnCablesChanged;
    }

    private void OnPlacementsChanged()
    {
        ConnectionsTab.RefreshDevicePinOptions();
        TerminationsTab.Refresh();
    }

    private void OnConnectionsChanged()
    {
        ConnectionsTab.Refresh();
        TerminationsTab.Refresh();
    }

    private void OnCablesChanged() => ConnectionsTab.RefreshCableOptions();

    public void Dispose()
    {
        _session.PlacementsChanged -= OnPlacementsChanged;
        _session.ConnectionsChanged -= OnConnectionsChanged;
        _session.CablesChanged -= OnCablesChanged;
        TerminationsTab.Dispose();
    }
}
