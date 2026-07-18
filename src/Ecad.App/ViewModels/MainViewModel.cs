using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ecad.App.Reports;
using Ecad.App.Services;
using Ecad.App.Views;
using Ecad.Core.Enums;
using Ecad.Core.Models;
using Ecad.Data;
using Ecad.Data.Import;
using Ecad.Reports;
using Ecad.Reports.Builders;
using Microsoft.Win32;

namespace Ecad.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private ProjectSession? _session;
    private ReportEngine? _reportEngine;

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
    [NotifyCanExecuteChangedFor(nameof(GenerateConnectionListReportCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateBomReportCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateCableOverviewReportCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateCableManufacturingSheetsCommand))]
    [NotifyCanExecuteChangedFor(nameof(RegenerateAllReportsCommand))]
    private bool _isProjectOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportEplanPartsCommand))]
    private bool _isImporting;

    public ObservableCollection<Page> Pages { get; } = [];
    public ObservableCollection<DocumentTabViewModel> OpenTabs { get; } = [];

    /// <summary>The Pages sidebar's current grouping — a per-project display preference (see
    /// Project.PageNavigatorSettingsJson), not project content.</summary>
    [ObservableProperty]
    private PageGroupBy _pageGroupBy = PageGroupBy.None;

    /// <summary>View > Page Navigator — in-memory only, not persisted (not asked for; resets to
    /// visible on every launch).</summary>
    [ObservableProperty]
    private bool _isPageNavigatorVisible = true;

    /// <summary>View > Devices Navigator — same in-memory-only scope cut as IsPageNavigatorVisible.
    /// Controls whether the Devices tab appears in the sidebar's tab strip at all (a collapsed
    /// TabItem is removed from the strip, not just hidden-but-present).</summary>
    [ObservableProperty]
    private bool _isDevicesNavigatorVisible = true;

    /// <summary>The Devices Navigator's data/commands — project-scoped, created in
    /// RefreshFromSession alongside Pages, moved here from the (now Devices-less) Grid Editor so it
    /// isn't duplicated between the sidebar and a Grid Editor tab.</summary>
    [ObservableProperty]
    private DevicesGridViewModel? _devicesNavigator;

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameSelectedPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenumberSelectedPagesCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedPagesCommand))]
    private IReadOnlyList<Page> _selectedPages = [];

    /// <summary>ListView.SelectedItems isn't a bindable dependency property — MainWindow's
    /// code-behind SelectionChanged handler pushes the current selection here (same shape as
    /// DataGrid.SelectedItems handling elsewhere in this app, ADR-014).</summary>
    public void UpdateSelectedPages(IReadOnlyList<Page> pages) => SelectedPages = pages;

    private bool CanRenameSelectedPage() => SelectedPages.Count == 1;

    /// <summary>Single-page-only, same scoping Rotate already uses for a canvas selection — a Page's
    /// own segments (especially PageNumberSegment) are per-page identity, not something several pages
    /// should be bulk-set to the same value.</summary>
    [RelayCommand(CanExecute = nameof(CanRenameSelectedPage))]
    private void RenameSelectedPage()
    {
        var page = SelectedPages[0];
        var dialog = new EditPageDialog(page) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;

        _session!.RenamePage(page.Id, dialog.FunctionSegment, dialog.LocationSegment,
            dialog.DocumentTypeSegment, dialog.PageNumberSegment, dialog.SelectedPageType);
    }

    private bool CanActOnSelectedPages() => SelectedPages.Count > 0;

    /// <summary>Auto-sequences Page # as 1, 2, 3... across the selected pages, in their current order in
    /// the Pages list (not click order) — same auto-sequential convention as "Renumber Wires", just
    /// scoped to this selection instead of the whole project.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnSelectedPages))]
    private void RenumberSelectedPages()
    {
        var selectedSet = new HashSet<Page>(SelectedPages);
        var orderedIds = Pages.Where(selectedSet.Contains).Select(p => p.Id).ToList();
        _session!.RenumberPages(orderedIds);
    }

    /// <summary>Cascade-deletes every selected page and everything drawn on it (symbols, wires,
    /// definition points, cable lines — see ProjectSession.DeletePagesCascade), closing any of their
    /// tabs first so nothing is left pointing at a page that no longer exists.</summary>
    [RelayCommand(CanExecute = nameof(CanActOnSelectedPages))]
    private void DeleteSelectedPages()
    {
        var result = MessageBox.Show(
            $"Delete {SelectedPages.Count} selected page(s)? This removes them — and every symbol, wire, definition point, and cable line on them — permanently. This cannot be undone.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var pageIds = SelectedPages.Select(p => p.Id).ToList();
        var pageIdSet = pageIds.ToHashSet();
        foreach (var tab in OpenTabs.Where(t => t.PageId is { } id && pageIdSet.Contains(id)).ToList())
        {
            (tab.Content as IDisposable)?.Dispose();
            OpenTabs.Remove(tab);
        }

        _session!.DeletePagesCascade(pageIds);
    }

    /// <summary>The Pages ListView's own settings gear — picks which segment "structural" group
    /// headers are built from (or none, the flat list). Grouping is a pure ICollectionView-level
    /// transform over the same Pages collection (PropertyGroupDescription + a matching SortDescription
    /// so groups appear alphabetically rather than in first-seen order) — no new data structures, and
    /// Pages.Clear()/Add() elsewhere (RefreshFromSession/OnPagesChanged) don't disturb it since they
    /// mutate the same collection instance the cached default view is attached to.</summary>
    [RelayCommand]
    private void SetPageGroupBy(PageGroupBy groupBy) => PageGroupBy = groupBy;

    partial void OnPageGroupByChanged(PageGroupBy value)
    {
        ApplyPageGrouping();
        PersistPageNavigatorSettings();
    }

    private void ApplyPageGrouping()
    {
        var view = CollectionViewSource.GetDefaultView(Pages);
        view.GroupDescriptions.Clear();
        view.SortDescriptions.Clear();

        var propertyName = PageGroupBy switch
        {
            PageGroupBy.Function => nameof(Page.FunctionSegment),
            PageGroupBy.Location => nameof(Page.LocationSegment),
            PageGroupBy.DocumentType => nameof(Page.DocumentTypeSegment),
            _ => null,
        };
        if (propertyName is null) return;

        view.GroupDescriptions.Add(new PropertyGroupDescription(propertyName));
        view.SortDescriptions.Add(new SortDescription(propertyName, ListSortDirection.Ascending));
    }

    private void LoadPageNavigatorSettings()
    {
        var json = _session!.CurrentProject.PageNavigatorSettingsJson;
        PageNavigatorSettings settings;
        try
        {
            settings = string.IsNullOrEmpty(json) ? new PageNavigatorSettings()
                : JsonSerializer.Deserialize<PageNavigatorSettings>(json) ?? new PageNavigatorSettings();
        }
        catch (JsonException)
        {
            settings = new PageNavigatorSettings();
        }

        PageGroupBy = settings.GroupBy; // triggers OnPageGroupByChanged -> ApplyPageGrouping + a harmless re-save of the same value
    }

    private void PersistPageNavigatorSettings()
    {
        var json = JsonSerializer.Serialize(new PageNavigatorSettings { GroupBy = PageGroupBy });
        _session!.UpdatePageNavigatorSettings(json);
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

        if (page.PageType == PageType.Report)
        {
            var reportViewModel = new ReportPageViewModel(_session, GetOrCreateReportEngine(), page);
            var reportTab = new DocumentTabViewModel { Header = FormatPageHeader(page), Content = reportViewModel, PageId = pageId };
            OpenTabs.Add(reportTab);
            SelectedTab = reportTab;
            return;
        }

        var viewModel = new SchematicPageViewModel(_session, page, focusPlacementId)
        {
            OwnerWindow = Application.Current.MainWindow,
        };
        viewModel.NavigateToPageRequested += (targetPageId, placementId) => OpenOrFocusPageTab(targetPageId, placementId);

        var tab = new DocumentTabViewModel { Header = FormatPageHeader(page), Content = viewModel, PageId = pageId };
        OpenTabs.Add(tab);
        SelectedTab = tab;
    }

    /// <summary>Loaded once and reused across projects — the report layout templates are app-level
    /// content (ReportTemplates/), not per-project data.</summary>
    private ReportEngine GetOrCreateReportEngine()
    {
        if (_reportEngine is not null) return _reportEngine;

        _reportEngine = new ReportEngine();
        _reportEngine.LoadTemplates(Path.Combine(AppContext.BaseDirectory, "ReportTemplates"));
        if (_reportEngine.LoadWarnings.Count > 0)
            StatusText = $"Report template warnings: {string.Join("; ", _reportEngine.LoadWarnings)}";
        return _reportEngine;
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void GenerateConnectionListReport()
    {
        GetOrCreateReportEngine();
        var result = _session!.UpsertGeneratedReportPage(ReportKinds.ConnectionList, ReportDocumentTypeSegments.ConnectionList, null, null);
        OpenOrFocusPageTab(result.Page.Id, null);
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void GenerateBomReport()
    {
        var dialog = new BomGroupingDialog { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;

        var groupingKey = dialog.SelectedMode switch
        {
            BomGroupingMode.PerLocation => "Location",
            BomGroupingMode.PerCableAssembly => "CableAssembly",
            _ => "Project",
        };

        GetOrCreateReportEngine();
        var result = _session!.UpsertGeneratedReportPage(ReportKinds.Bom, ReportDocumentTypeSegments.Bom, null, groupingKey);
        OpenOrFocusPageTab(result.Page.Id, null);
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void GenerateCableOverviewReport()
    {
        GetOrCreateReportEngine();
        var result = _session!.UpsertGeneratedReportPage(ReportKinds.CableOverview, ReportDocumentTypeSegments.CableOverview, null, null);
        OpenOrFocusPageTab(result.Page.Id, null);
    }

    /// <summary>Batch: one manufacturing-sheet page per Cable in the project. Also removes any such page
    /// whose Cable no longer exists (renamed/deleted since the last run) — the same orphan cleanup
    /// DeleteCable itself performs for a single cable.</summary>
    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void GenerateCableManufacturingSheets()
    {
        GetOrCreateReportEngine();
        var cables = _session!.GetAllCables();
        _session.DeleteOrphanedCableManufacturingSheets(cables.Select(c => c.Id).ToList());

        long? firstPageId = null;
        foreach (var cable in cables)
        {
            var result = _session.UpsertGeneratedReportPage(ReportKinds.CableManufacturingSheet, ReportDocumentTypeSegments.CableManufacturingSheet, cable.Id, null);
            firstPageId ??= result.Page.Id;
        }

        StatusText = $"Generated {cables.Count} manufacturing sheet(s).";
        if (firstPageId is { } id) OpenOrFocusPageTab(id, null);
    }

    /// <summary>Re-renders every currently open report tab against the latest session data — since a
    /// report page's content is never cached (ReportPageViewModel re-runs its Builder on open/regenerate
    /// alike), this needs no ProjectSession involvement at all, just re-triggering each open tab.</summary>
    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void RegenerateAllReports()
    {
        foreach (var tab in OpenTabs)
        {
            if (tab.Content is ReportPageViewModel reportViewModel)
                reportViewModel.RegenerateCommand.Execute(null);
        }
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

    [RelayCommand]
    private static void OpenSettings()
    {
        var dialog = new SettingsDialog { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
    }

    private void RefreshFromSession()
    {
        WindowTitle = $"ECAD — {_session!.CurrentProject.Name}";
        _session.PagesChanged += OnPagesChanged;
        Pages.Clear();
        foreach (var page in _session.Pages) Pages.Add(page);
        IsProjectOpen = true;
        LoadPageNavigatorSettings();

        DevicesNavigator = new DevicesGridViewModel(_session);
        DevicesNavigator.NavigateToPageRequested += (pageId, placementId) => OpenOrFocusPageTab(pageId, placementId);
        _session.PlacementsChanged += RefreshDevicesNavigator;
    }

    private void RefreshDevicesNavigator() => DevicesNavigator?.Refresh();

    /// <summary>Keeps the Pages list panel in sync with report pages created/reused/removed by
    /// UpsertGeneratedReportPage, DeleteOrphanedCableManufacturingSheets, or a cable-delete's report
    /// cleanup — none of which go through the AddPage command's own manual Pages.Add.</summary>
    private void OnPagesChanged()
    {
        Pages.Clear();
        foreach (var page in _session!.Pages) Pages.Add(page);
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

        DevicesNavigator?.Dispose();
        DevicesNavigator = null;
    }

    public void Dispose()
    {
        foreach (var tab in OpenTabs.ToList())
        {
            (tab.Content as IDisposable)?.Dispose();
            OpenTabs.Remove(tab);
        }

        DevicesNavigator?.Dispose();
        _session?.Dispose();
    }
}
