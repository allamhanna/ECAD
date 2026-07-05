using Microsoft.Data.Sqlite;

namespace Ecad.Data.Tests;

/// <summary>A fresh temp-file path per test, deleted on dispose. Used instead of ':memory:' so
/// ProjectDatabase/LibraryDatabase's real "open file, create if missing" path is exercised.</summary>
public sealed class TempSqliteFile : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ecad-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        // Microsoft.Data.Sqlite pools native file handles even after SqliteConnection.Dispose();
        // clear the pool first so the underlying file is actually released before deletion.
        SqliteConnection.ClearAllPools();

        foreach (var candidate in new[] { Path, Path + "-wal", Path + "-shm", Path + "-journal" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }
}
