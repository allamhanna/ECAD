using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Ecad.Data;

/// <summary>
/// Applies numbered .sql migration files embedded under Migrations/{set}/ to a SQLite database,
/// tracking applied versions in a schema_migrations table. No ORM migration framework — plain,
/// readable SQL files, applied once each, in order.
/// </summary>
public static class MigrationRunner
{
    private static readonly Regex FileNamePattern = new(@"Migrations\.(?<set>[^.]+)\.(?<version>\d+)_.*\.sql$", RegexOptions.Compiled);

    public static void Apply(SqliteConnection connection, string migrationSet)
    {
        using var ensureTable = connection.CreateCommand();
        ensureTable.CommandText = "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, applied_at_utc TEXT NOT NULL);";
        ensureTable.ExecuteNonQuery();

        var applied = new HashSet<int>();
        using (var readApplied = connection.CreateCommand())
        {
            readApplied.CommandText = "SELECT version FROM schema_migrations;";
            using var reader = readApplied.ExecuteReader();
            while (reader.Read())
                applied.Add(reader.GetInt32(0));
        }

        var assembly = Assembly.GetExecutingAssembly();
        var migrations = assembly.GetManifestResourceNames()
            .Select(name => (name, match: FileNamePattern.Match(name)))
            .Where(x => x.match.Success && x.match.Groups["set"].Value == migrationSet)
            .Select(x => (x.name, version: int.Parse(x.match.Groups["version"].Value)))
            .OrderBy(x => x.version)
            .ToList();

        foreach (var (resourceName, version) in migrations)
        {
            if (applied.Contains(version)) continue;

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded migration resource not found: {resourceName}");
            using var streamReader = new StreamReader(stream);
            var sql = streamReader.ReadToEnd();

            using var transaction = connection.BeginTransaction();
            using (var runMigration = connection.CreateCommand())
            {
                runMigration.Transaction = transaction;
                runMigration.CommandText = sql;
                runMigration.ExecuteNonQuery();
            }
            using (var recordVersion = connection.CreateCommand())
            {
                recordVersion.Transaction = transaction;
                recordVersion.CommandText = "INSERT INTO schema_migrations (version, applied_at_utc) VALUES ($version, $now);";
                recordVersion.Parameters.AddWithValue("$version", version);
                recordVersion.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                recordVersion.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }
}
