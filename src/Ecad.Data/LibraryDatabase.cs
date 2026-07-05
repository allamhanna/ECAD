using Microsoft.Data.Sqlite;

namespace Ecad.Data;

/// <summary>Opens (creating if needed) the shared parts/symbol library SQLite database and applies Library migrations.</summary>
public static class LibraryDatabase
{
    public static string DefaultFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ecad", "library.db");

    public static SqliteConnection Open(string? filePath = null)
    {
        filePath ??= DefaultFilePath;
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var connection = new SqliteConnection($"Data Source={filePath}");
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }
        MigrationRunner.Apply(connection, "Library");
        return connection;
    }
}
