using Microsoft.Data.Sqlite;

namespace Ecad.Data;

/// <summary>Opens (creating if needed) a project's single-file SQLite database and applies Project migrations.</summary>
public static class ProjectDatabase
{
    public static SqliteConnection Open(string filePath)
    {
        var connection = new SqliteConnection($"Data Source={filePath}");
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }
        MigrationRunner.Apply(connection, "Project");
        return connection;
    }
}
