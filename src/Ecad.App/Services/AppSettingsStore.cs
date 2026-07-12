using System.IO;
using System.Text.Json;

namespace Ecad.App.Services;

/// <summary>Tiny persisted app-level state — currently just "which project to auto-reopen on
/// launch." Lives at %LOCALAPPDATA%\Ecad\settings.json, same path-building idiom as
/// LibraryDatabase.DefaultFilePath (just JSON instead of SQLite — there's no shared/relational data
/// to justify a database here).</summary>
public sealed class AppSettings
{
    public string? LastOpenedProjectPath { get; set; }

    /// <summary>True once the user explicitly runs Close Project — auto-reopen on next launch checks
    /// this so an intentional close isn't immediately undone by the app reopening the same project.</summary>
    public bool WasExplicitlyClosed { get; set; }
}

public static class AppSettingsStore
{
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ecad", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // Malformed/unreadable settings should never block the app from starting — falling back
            // to a fresh default is fine, not worth surfacing to the user.
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
    }
}
