using Ecad.Data;

namespace Ecad.App.ViewModels;

/// <summary>
/// Composition root for the M8 Grid Editor window: one instance per open project, hosting the
/// Connections/Cables/Terminations tab view-models (Devices moved to the sidebar's Devices
/// Navigator — MainViewModel.DevicesNavigator — so it isn't duplicated here). Subscribes to
/// ProjectSession's live-sync events once and fans out to whichever tab(s) hold derived data — same
/// cross-window live-sync pattern PlacementsChanged/ConnectionsChanged already established for
/// SchematicPageWindow (ADR-008/009).
/// </summary>
public sealed class GridEditorViewModel : IDisposable
{
    private readonly ProjectSession _session;

    public ConnectionsGridViewModel ConnectionsTab { get; }
    public CablesGridViewModel CablesTab { get; }
    public TerminationsGridViewModel TerminationsTab { get; }

    public GridEditorViewModel(ProjectSession session)
    {
        _session = session;
        ConnectionsTab = new ConnectionsGridViewModel(session);
        CablesTab = new CablesGridViewModel(session);
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

    private void OnCablesChanged()
    {
        CablesTab.Refresh();
        ConnectionsTab.RefreshCableOptions();
    }

    public void Dispose()
    {
        _session.PlacementsChanged -= OnPlacementsChanged;
        _session.ConnectionsChanged -= OnConnectionsChanged;
        _session.CablesChanged -= OnCablesChanged;
        TerminationsTab.Dispose();
    }
}
