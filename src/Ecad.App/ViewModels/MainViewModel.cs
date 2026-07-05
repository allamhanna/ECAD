using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private bool _isProjectOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportEplanPartsCommand))]
    private bool _isImporting;

    public ObservableCollection<Page> Pages { get; } = [];

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
    }

    [RelayCommand]
    private void OpenProject()
    {
        var openDialog = new OpenFileDialog { Filter = "ECAD Project (*.ecad)|*.ecad" };
        if (openDialog.ShowDialog() != true) return;

        CloseCurrentSession();
        _session = ProjectSession.Open(openDialog.FileName);
        RefreshFromSession();
        StatusText = $"Opened {openDialog.FileName}";
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
    }

    [RelayCommand(CanExecute = nameof(IsProjectOpen))]
    private void CloseProject()
    {
        CloseCurrentSession();
        WindowTitle = "ECAD";
        StatusText = "No project open.";
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

    [RelayCommand]
    private static void Exit() => Application.Current.Shutdown();

    private void RefreshFromSession()
    {
        WindowTitle = $"ECAD — {_session!.CurrentProject.Name}";
        Pages.Clear();
        foreach (var page in _session.Pages) Pages.Add(page);
        IsProjectOpen = true;
    }

    private void CloseCurrentSession()
    {
        _session?.Dispose();
        _session = null;
        Pages.Clear();
        IsProjectOpen = false;
    }

    public void Dispose() => CloseCurrentSession();
}
