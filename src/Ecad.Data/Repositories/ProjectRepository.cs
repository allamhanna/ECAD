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

    private sealed record ProjectRow(long Id, string Name, string? Customer, string? ProjectNumber, string? Revision,
        string CreatedAtUtc, string? PageStructureSettingsJson, string? NumberingSettingsJson)
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
