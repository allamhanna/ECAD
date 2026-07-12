using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ecad.App.Services;
using Ecad.App.Views;
using Ecad.Core.Models;
using Ecad.Data;
using Ecad.Data.Import;
using Microsoft.Win32;

namespace Ecad.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private ProjectSession? _session;

    [ObservableProperty]
    private string _windowTitle = "ECAD";

    [ObservableProperty]
    private string _statusText = "No project open.";

    // RelayCommand doesn't auto-requery on property changes — without these attributes, Save/SaveAs/
    // CloseProject/AddPage stay in whatever CanExecute state they had when first queried (disabled),
    // regardless of IsProjectOpen actually changing afterward.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveAsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenGridEditorCommand))]
    private bool _isProjectOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportEplanPartsCommand))]
    private bool _isImporting;

    public ObservableCollection<Page> Pages { get; } = [];
    public ObservableCollection<DocumentTabViewModel> OpenTabs { get; } = [];

    [ObservableProperty]
    private DocumentTabViewModel? _selectedTab;

    [RelayCommand]
    private void NewProject()
    {
        var dialog = new NewProjectDialog { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;

        var saveDialog = new SaveFileDialog
        {
            Filter = "ECAD Project (*.ecad)|*.ecad",
            DefaultExt = ".ecad",
            FileName = dialog.ProjectName,
        };
        if (saveDialog.ShowDialog() != true) return;

        CloseCurrentSession();
        _session = ProjectSession.Create(saveDialog.FileName, new Project
        {
            Name = dialog.ProjectName,
            Customer = dialog.Customer,
            ProjectNumber = dialog.ProjectNumber,
            Revision = dialog.Revision,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        RefreshFromSession();
        StatusText = $"Created {saveDialog.FileName}";
        SaveLastOpenedProject(saveDialog.FileName);
    }

    [RelayCommand]
    private void OpenProject()
    {
        var openDialog = new OpenFileDialog { Filter = "ECAD Project (*.ecad)|*.ecad" };
        if (openDialog.ShowDialog() != true) return;

        OpenProjectFromPath(openDialog.FileName);
    }

    /// <summary>Shared by the Open Project dialog and auto-reopen-on-launch.</summary>
    private void OpenProjectFromPath(string path)
    {
        CloseCurrentSession();
        _session = ProjectSession.Open(path);
        RefreshFromSession();
        StatusText = $"Opened {path}";
        SaveLastOpenedProject(path);
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void Save()
    {
        _session!.Checkpoint();
        StatusText = $"Saved at {DateTimeOffset.Now:T}";
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void SaveAs()
    {
        var saveDialog = new SaveFileDialog
        {
            Filter = "ECAD Project (*.ecad)|*.ecad",
            DefaultExt = ".ecad",
            FileName = _session!.CurrentProject.Name,
        };
        if (saveDialog.ShowDialog() != true) return;

        _session = _session.SaveAs(saveDialog.FileName);
        StatusText = $"Saved as {saveDialog.FileName}";
        SaveLastOpenedProject(saveDialog.FileName);
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void CloseProject()
    {
        CloseCurrentSession();
        WindowTitle = "ECAD";
        StatusText = "No project open.";

        // Marks this as an intentional close, so TryAutoReopenLastProject doesn't immediately
        // reopen the same project the next time the app launches.
        var settings = AppSettingsStore.Load();
        settings.WasExplicitlyClosed = true;
        AppSettingsStore.Save(settings);
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void AddPage()
    {
        var dialog = new AddPageDialog { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;

        var page = _session!.AddPage(new Page
        {
            FunctionSegment = dialog.FunctionSegment,
            LocationSegment = dialog.LocationSegment,
            DocumentTypeSegment = dialog.DocumentTypeSegment,
            PageNumberSegment = dialog.PageNumberSegment,
            PageType = dialog.SelectedPageType,
        });
        Pages.Add(page);
    }

    [RelayCommand(CanExecute = nameof(CanImportEplanParts))]
    private async Task ImportEplanPartsAsync()
    {
        var openDialog = new OpenFileDialog { Filter = "EPLAN Parts Export (*.edz)|*.edz" };
        if (openDialog.ShowDialog() != true) return;

        IsImporting = true;
        StatusText = "Importing EPLAN parts...";
        try
        {
            var result = await Task.Run(() =>
            {
                using var libraryConnection = LibraryDatabase.Open();
                return EplanEdzImporter.Import(openDialog.FileName, libraryConnection);
            });

            StatusText = $"Import complete: {result.PartsAdded} added, {result.PartsUpdated} updated, {result.PartsUnchanged} unchanged.";
            var message = $"Added: {result.PartsAdded}\nUpdated: {result.PartsUpdated}\nUnchanged: {result.PartsUnchanged}";
            if (result.Warnings.Count > 0)
                message += $"\n\nWarnings ({result.Warnings.Count}):\n" + string.Join("\n", result.Warnings.Take(20));
            MessageBox.Show(message, "EPLAN Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = "Import failed.";
            MessageBox.Show(ex.ToString(), "EPLAN Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsImporting = false;
        }
    }

    private bool CanImportEplanParts() => !IsImporting;

    public void OpenPage(Page page) => OpenOrFocusPageTab(page.Id, focusPlacementId: null);

    /// <summary>Finds this page's already-open tab and selects it (optionally focusing a placement —
    /// Ctrl+Click cross-reference navigation), or creates a new one. The single entry point for "show
    /// me this page" (M10: replaces SchematicPageWindow.OpenOrFocus's per-page window registry) — both
    /// MainWindow's double-click-a-page and SchematicPageViewModel.NavigateToPageRequested route here.</summary>
    public void OpenOrFocusPageTab(long pageId, long? focusPlacementId)
    {
        var existing = OpenTabs.FirstOrDefault(t => t.PageId == pageId);
        if (existing is not null)
        {
            SelectedTab = existing;
            if (focusPlacementId is { } id && existing.Content is SchematicPageViewModel existingViewModel)
                existingViewModel.FocusPlacement(id);
            return;
        }

        var page = _session!.Pages.FirstOrDefault(p => p.Id == pageId);
        if (page is null) return;

        var viewModel = new SchematicPageViewModel(_session, page, focusPlacementId)
        {
            OwnerWindow = Application.Current.MainWindow,
        };
        viewModel.NavigateToPageRequested += (targetPageId, placementId) => OpenOrFocusPageTab(targetPageId, placementId);

        var tab = new DocumentTabViewModel { Header = FormatPageHeader(page), Content = viewModel, PageId = pageId };
        OpenTabs.Add(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private void CloseTab(DocumentTabViewModel tab)
    {
        (tab.Content as IDisposable)?.Dispose();
        OpenTabs.Remove(tab);
    }

    private static string FormatPageHeader(Page page)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(page.FunctionSegment)) parts.Add($"={page.FunctionSegment}");
        if (!string.IsNullOrEmpty(page.LocationSegment)) parts.Add($"+{page.LocationSegment}");
        if (!string.IsNullOrEmpty(page.DocumentTypeSegment)) parts.Add($"&{page.DocumentTypeSegment}");
        if (!string.IsNullOrEmpty(page.PageNumberSegment)) parts.Add($"/{page.PageNumberSegment}");
        return parts.Count > 0 ? string.Join(" ", parts) : $"Page {page.Id}";
    }

    /// <summary>Grid Editor is project-scoped and a singleton, like the schematic-page tabs' own
    /// find-or-create shape (OpenOrFocusPageTab) but keyed by ViewModel type instead of PageId, since
    /// there's only ever one Grid Editor per open project.</summary>
    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void OpenGridEditor()
    {
        var existing = OpenTabs.FirstOrDefault(t => t.Content is GridEditorViewModel);
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        var viewModel = new GridEditorViewModel(_session!);
        var tab = new DocumentTabViewModel { Header = "Grid Editor", Content = viewModel };
        OpenTabs.Add(tab);
        SelectedTab = tab;
    }

    /// <summary>Parts Library and Symbol Browser are project-independent singletons (IsProjectScoped
    /// = false), so they survive CloseCurrentSession — same behavior as the old PartsLibraryWindow/
    /// SymbolBrowserWindow, which stayed open across a project close since neither ever held a
    /// ProjectSession reference.</summary>
    [RelayCommand]
    private void OpenPartsLibrary()
    {
        var existing = OpenTabs.FirstOrDefault(t => t.Content is PartsLibraryViewModel);
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        var tab = new DocumentTabViewModel { Header = "Parts Library", Content = new PartsLibraryViewModel(), IsProjectScoped = false };
        OpenTabs.Add(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private void OpenSymbolBrowser()
    {
        var existing = OpenTabs.FirstOrDefault(t => t.Content is SymbolBrowserViewModel);
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        var tab = new DocumentTabViewModel { Header = "Symbol Browser", Content = new SymbolBrowserViewModel(), IsProjectScoped = false };
        OpenTabs.Add(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private static void Exit() => Application.Current.Shutdown();

    private void RefreshFromSession()
    {
        WindowTitle = $"ECAD — {_session!.CurrentProject.Name}";
        Pages.Clear();
        foreach (var page in _session.Pages) Pages.Add(page);
        IsProjectOpen = true;
    }

    private static void SaveLastOpenedProject(string path) =>
        AppSettingsStore.Save(new AppSettings { LastOpenedProjectPath = path, WasExplicitlyClosed = false });

    /// <summary>Called once from MainWindow's Loaded event (not the constructor — this needs
    /// Application.Current.MainWindow to already be set, so a page opened here can correctly own its
    /// dialogs against it). Reopens the last project unless it was explicitly closed last session, and
    /// opens its first page as a tab. Guards with File.Exists first: Microsoft.Data.Sqlite silently
    /// creates an empty file at a missing path rather than throwing, so ProjectSession.Open must never
    /// be called with an unchecked stale path — this would otherwise leave a junk .ecad file behind.
    /// Any other failure degrades to "No project open" plus a status message, never an unhandled
    /// exception.</summary>
    public void TryAutoReopenLastProject()
    {
        var settings = AppSettingsStore.Load();
        if (settings.WasExplicitlyClosed) return;
        if (string.IsNullOrEmpty(settings.LastOpenedProjectPath)) return;
        if (!File.Exists(settings.LastOpenedProjectPath)) return;

        try
        {
            OpenProjectFromPath(settings.LastOpenedProjectPath);
            if (_session!.Pages.Count > 0) OpenPage(_session.Pages[0]);
        }
        catch (Exception ex)
        {
            CloseCurrentSession();
            WindowTitle = "ECAD";
            StatusText = $"Could not reopen last project: {ex.Message}";
        }
    }

    private void CloseCurrentSession()
    {
        foreach (var tab in OpenTabs.Where(t => t.IsProjectScoped).ToList())
        {
            (tab.Content as IDisposable)?.Dispose();
            OpenTabs.Remove(tab);
        }

        _session?.Dispose();
        _session = null;
        Pages.Clear();
        IsProjectOpen = false;
    }

    public void Dispose()
    {
        foreach (var tab in OpenTabs.ToList())
        {
            (tab.Content as IDisposable)?.Dispose();
            OpenTabs.Remove(tab);
        }

        _session?.Dispose();
    }
}
