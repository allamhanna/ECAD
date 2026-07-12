using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ecad.Core.Models;
using Ecad.Data;

namespace Ecad.App.ViewModels;

/// <summary>M8 Cables grid (Section 6.2): create/edit cables and their core rows, without drawing.
/// Deliberately stays at plain field-level CRUD — end-type classification logic and core-to-connection
/// "smart" assignment workflows are M9's scope (see PROGRESS.md), not this one's.</summary>
public sealed partial class CablesGridViewModel : ObservableObject
{
    private readonly ProjectSession _session;

    public ObservableCollection<Cable> Cables { get; } = [];
    public ObservableCollection<Cable> SelectedCables { get; } = [];
    public ObservableCollection<CableCore> Cores { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCoreCommand))]
    private CableCore? _selectedCore;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCoreCommand))]
    private Cable? _selectedCable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCableCommand))]
    private string _newTag = string.Empty;

    public CablesGridViewModel(ProjectSession session)
    {
        _session = session;
        Refresh();
    }

    public void Refresh()
    {
        var previouslySelectedId = SelectedCable?.Id;
        Cables.Clear();
        foreach (var cable in _session.GetAllCables()) Cables.Add(cable);
        SelectedCable = previouslySelectedId is null ? null : Cables.FirstOrDefault(c => c.Id == previouslySelectedId);
    }

    partial void OnSelectedCableChanged(Cable? value)
    {
        Cores.Clear();
        if (value is null) return;
        foreach (var core in _session.GetCableCores(value.Id)) Cores.Add(core);
    }

    [RelayCommand(CanExecute = nameof(CanAddCable))]
    private void AddCable()
    {
        _session.CreateCable(new Cable { Tag = NewTag });
        NewTag = string.Empty;
    }

    private bool CanAddCable() => !string.IsNullOrWhiteSpace(NewTag);

    /// <summary>M8 grid deletes have no undo (ADR-010) — a confirmation prompt is the safety net.
    /// Gated by CanDeleteSelectedCables (not just "any selection") so the button disables outright
    /// when nothing selected actually qualifies, rather than silently doing nothing when clicked
    /// (ADR-013 — the same class of bug found and fixed on the Devices tab's Delete Selected).</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSelectedCables))]
    private void DeleteSelectedCables()
    {
        var selected = SelectedCables.ToList();
        var deletable = selected.Where(c => _session.CanDeleteCable(c.Id)).ToList();
        var skipped = selected.Count - deletable.Count;

        var message = $"Delete {deletable.Count} selected cable(s)? This cannot be undone.";
        if (skipped > 0)
            message += $"\n\n{skipped} selected cable(s) are still referenced by a Connection and will be skipped — un-assign them on the Connections tab first.";

        var result = MessageBox.Show(message, "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        foreach (var cable in deletable) _session.DeleteCable(cable.Id);
    }

    private bool CanDeleteSelectedCables() => SelectedCables.Any(c => _session.CanDeleteCable(c.Id));

    public void NotifyCableSelectionChanged() => DeleteSelectedCablesCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(HasSelectedCable))]
    private void AddCore()
    {
        var nextNumber = Cores.Count == 0 ? 1 : Cores.Max(c => c.CoreNumber) + 1;
        var core = _session.AddCableCore(SelectedCable!.Id, new CableCore { CoreNumber = nextNumber });
        Cores.Add(core);
    }

    private bool HasSelectedCable() => SelectedCable is not null;

    [RelayCommand(CanExecute = nameof(HasSelectedCore))]
    private void DeleteSelectedCore()
    {
        var result = MessageBox.Show("Delete the selected core? Any connection assigned to it will be un-assigned, not deleted.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _session.DeleteCableCore(SelectedCore!.Id);
        Cores.Remove(SelectedCore);
    }

    private bool HasSelectedCore() => SelectedCore is not null;

    /// <summary>Called by the view after a DataGrid row edit commits — Cable/CableCore have plain
    /// properties, so nothing persists the in-memory edit to SQLite without this.</summary>
    public void CommitCableEdit(Cable cable) => _session.UpdateCable(cable);

    public void CommitCoreEdit(CableCore core) => _session.UpdateCableCore(core);
}
