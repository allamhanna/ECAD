using Dapper;
using Ecad.Core.Models;
using Microsoft.Data.Sqlite;

namespace Ecad.Data.Repositories;

public class ProjectRepository(SqliteConnection connection)
{
    public long InsertProject(Project project)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO Project (Name, Customer, ProjectNumber, Revision, CreatedAtUtc, PageStructureSettingsJson, NumberingSettingsJson)
            VALUES (@Name, @Customer, @ProjectNumber, @Revision, @CreatedAtUtc, @PageStructureSettingsJson, @NumberingSettingsJson)
            RETURNING Id;
            """,
            new
            {
                project.Name,
                project.Customer,
                project.ProjectNumber,
                project.Revision,
                CreatedAtUtc = project.CreatedAtUtc.ToString("O"),
                project.PageStructureSettingsJson,
                project.NumberingSettingsJson,
            });
    }

    /// <summary>A Project DB is expected to hold exactly one Project row; this is how the app loads it on open.</summary>
    public Project? GetFirstProject()
    {
        return connection.QuerySingleOrDefault<ProjectRow>(
            "SELECT * FROM Project ORDER BY Id LIMIT 1;")?.ToModel();
    }

    public Project? GetProject(long id)
    {
        return connection.QuerySingleOrDefault<ProjectRow>(
            "SELECT * FROM Project WHERE Id = @id;", new { id })?.ToModel();
    }

    public long InsertPage(Page page)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO Page (ProjectId, FunctionSegment, LocationSegment, DocumentTypeSegment, PageNumberSegment, PageType, FrameFormat, SortOrder)
            VALUES (@ProjectId, @FunctionSegment, @LocationSegment, @DocumentTypeSegment, @PageNumberSegment, @PageType, @FrameFormat, @SortOrder)
            RETURNING Id;
            """,
            new
            {
                page.ProjectId,
                page.FunctionSegment,
                page.LocationSegment,
                page.DocumentTypeSegment,
                page.PageNumberSegment,
                PageType = (int)page.PageType,
                page.FrameFormat,
                page.SortOrder,
            });
    }

    public Page? GetPage(long id)
    {
        return connection.QuerySingleOrDefault<PageRow>(
            "SELECT * FROM Page WHERE Id = @id;", new { id })?.ToModel();
    }

    public IReadOnlyList<Page> GetPages(long projectId)
    {
        return connection.Query<PageRow>(
            "SELECT * FROM Page WHERE ProjectId = @projectId ORDER BY SortOrder, Id;", new { projectId })
            .Select(r => r.ToModel())
            .ToList();
    }

    /// <summary>Highest SortOrder currently used among a project's pages, for appending a new one after
    /// them (M12: report pages are appended, never inserted mid-sequence).</summary>
    public int GetMaxSortOrder(long projectId)
    {
        return (int)connection.ExecuteScalar<long>(
            "SELECT IFNULL(MAX(SortOrder), 0) FROM Page WHERE ProjectId = @projectId;", new { projectId });
    }

    /// <summary>M12: first Page update path in the codebase — used only to bump a generated report
    /// page's PageNumberSegment/SortOrder in place; report pages never change Function/Location/
    /// DocumentType/PageType after creation.</summary>
    public void UpdatePage(Page page)
    {
        connection.Execute(
            """
            UPDATE Page SET FunctionSegment = @FunctionSegment, LocationSegment = @LocationSegment,
                DocumentTypeSegment = @DocumentTypeSegment, PageNumberSegment = @PageNumberSegment,
                PageType = @PageTypeValue, FrameFormat = @FrameFormat, SortOrder = @SortOrder
            WHERE Id = @Id;
            """,
            new
            {
                page.Id,
                page.FunctionSegment,
                page.LocationSegment,
                page.DocumentTypeSegment,
                page.PageNumberSegment,
                PageTypeValue = (int)page.PageType,
                page.FrameFormat,
                page.SortOrder,
            });
    }

    /// <summary>M12: first Page delete path in the codebase — used to remove a manufacturing-sheet
    /// report page whose Cable no longer exists. GeneratedReport rows cascade with their Page.</summary>
    public void DeletePage(long pageId)
    {
        connection.Execute("DELETE FROM Page WHERE Id = @pageId;", new { pageId });
    }

    /// <summary>Persists the Page Navigator's chosen grouping — a UI display preference kept
    /// per-project (see Project.PageNavigatorSettingsJson), not fanned out via any live-sync event
    /// since MainViewModel is the only reader/writer.</summary>
    public void UpdatePageNavigatorSettings(long projectId, string? json)
    {
        connection.Execute("UPDATE Project SET PageNavigatorSettingsJson = @json WHERE Id = @projectId;", new { projectId, json });
    }

    private sealed record ProjectRow(long Id, string Name, string? Customer, string? ProjectNumber, string? Revision,
        string CreatedAtUtc, string? PageStructureSettingsJson, string? NumberingSettingsJson, string? PageNavigatorSettingsJson)
    {
        public Project ToModel() => new()
        {
            Id = Id,
            Name = Name,
            Customer = Customer,
            ProjectNumber = ProjectNumber,
            Revision = Revision,
            CreatedAtUtc = DateTimeOffset.Parse(CreatedAtUtc),
            PageStructureSettingsJson = PageStructureSettingsJson,
            NumberingSettingsJson = NumberingSettingsJson,
            PageNavigatorSettingsJson = PageNavigatorSettingsJson,
        };
    }

    // long rather than int to match Dapper's exact-type-match constructor materialization against
    // SQLite's underlying INTEGER reader type (see PartRepository.PartRow for detail).
    private sealed record PageRow(long Id, long ProjectId, string? FunctionSegment, string? LocationSegment,
        string? DocumentTypeSegment, string? PageNumberSegment, long PageType, string FrameFormat, long SortOrder)
    {
        public Page ToModel() => new()
        {
            Id = Id,
            ProjectId = ProjectId,
            FunctionSegment = FunctionSegment,
            LocationSegment = LocationSegment,
            DocumentTypeSegment = DocumentTypeSegment,
            PageNumberSegment = PageNumberSegment,
            PageType = (Core.Enums.PageType)(int)PageType,
            FrameFormat = FrameFormat,
            SortOrder = (int)SortOrder,
        };
    }
}
