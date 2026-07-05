using Ecad.Core.Models;
using Ecad.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace Ecad.Data;

/// <summary>
/// An open project: the SQLite connection to its .ecad file plus the currently loaded Project
/// row and its Pages. Plain C#, no UI dependency, so it's unit-testable — Ecad.App's
/// MainViewModel is a thin wrapper over this that adds file-picker dialogs.
/// </summary>
public sealed class ProjectSession : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ProjectRepository _projects;
    private readonly List<Page> _pages;

    private ProjectSession(string filePath, SqliteConnection connection, Project project, List<Page> pages)
    {
        FilePath = filePath;
        _connection = connection;
        _projects = new ProjectRepository(connection);
        CurrentProject = project;
        _pages = pages;
    }

    public string FilePath { get; }
    public Project CurrentProject { get; private set; }
    public IReadOnlyList<Page> Pages => _pages;

    /// <summary>Creates a new .ecad file at the given path and inserts the given Project as its single Project row.</summary>
    public static ProjectSession Create(string filePath, Project project)
    {
        var connection = ProjectDatabase.Open(filePath);
        var projects = new ProjectRepository(connection);
        project.Id = projects.InsertProject(project);
        return new ProjectSession(filePath, connection, project, []);
    }

    /// <summary>Opens an existing .ecad file and loads its Project row and Pages.</summary>
    public static ProjectSession Open(string filePath)
    {
        var connection = ProjectDatabase.Open(filePath);
        try
        {
            var projects = new ProjectRepository(connection);
            var project = projects.GetFirstProject()
                ?? throw new InvalidOperationException($"'{filePath}' has no Project row.");
            var pages = projects.GetPages(project.Id).ToList();
            return new ProjectSession(filePath, connection, project, pages);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public Page AddPage(Page page)
    {
        page.ProjectId = CurrentProject.Id;
        page.Id = _projects.InsertPage(page);
        _pages.Add(page);
        return page;
    }

    /// <summary>Flushes any WAL contents to the main database file. Writes already commit immediately
    /// on each repository call — this exists so File &gt; Save is a real, truthful action.</summary>
    public void Checkpoint()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint;";
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
