using Dapper;
using Ecad.Core.Models;
using Microsoft.Data.Sqlite;

namespace Ecad.Data.Repositories;

public class GeneratedReportRepository(SqliteConnection connection)
{
    public long Insert(GeneratedReport report)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO GeneratedReport (PageId, ReportKind, SourceEntityId, GroupingKey, GeneratedAtUtc)
            VALUES (@PageId, @ReportKind, @SourceEntityId, @GroupingKey, @GeneratedAtUtcValue)
            RETURNING Id;
            """,
            new
            {
                report.PageId,
                report.ReportKind,
                report.SourceEntityId,
                report.GroupingKey,
                GeneratedAtUtcValue = report.GeneratedAtUtc.ToString("O"),
            });
    }

    /// <summary>Looks up by the same (ReportKind, SourceEntityId, GroupingKey) identity the unique
    /// index enforces — the "find the existing page to reuse" half of regeneration.</summary>
    public GeneratedReport? GetByIdentity(string reportKind, long? sourceEntityId, string? groupingKey)
    {
        return connection.QuerySingleOrDefault<GeneratedReportRow>(
            """
            SELECT * FROM GeneratedReport
            WHERE ReportKind = @reportKind
              AND IFNULL(SourceEntityId, -1) = IFNULL(@sourceEntityId, -1)
              AND IFNULL(GroupingKey, '') = IFNULL(@groupingKey, '');
            """,
            new { reportKind, sourceEntityId, groupingKey })?.ToModel();
    }

    public GeneratedReport? GetByPageId(long pageId)
    {
        return connection.QuerySingleOrDefault<GeneratedReportRow>(
            "SELECT * FROM GeneratedReport WHERE PageId = @pageId;", new { pageId })?.ToModel();
    }

    /// <summary>Every GeneratedReport row of a given kind, project-wide — used to find manufacturing-
    /// sheet pages whose Cable no longer exists (batch regeneration's orphan cleanup).</summary>
    public IReadOnlyList<GeneratedReport> GetAllForKind(long projectId, string reportKind)
    {
        return connection.Query<GeneratedReportRow>(
            """
            SELECT gr.* FROM GeneratedReport gr
            JOIN Page pg ON pg.Id = gr.PageId
            WHERE pg.ProjectId = @projectId AND gr.ReportKind = @reportKind;
            """,
            new { projectId, reportKind }).Select(r => r.ToModel()).ToList();
    }

    public void UpdateGeneratedAt(long id, DateTimeOffset generatedAtUtc)
    {
        connection.Execute(
            "UPDATE GeneratedReport SET GeneratedAtUtc = @generatedAtUtcValue WHERE Id = @id;",
            new { id, generatedAtUtcValue = generatedAtUtc.ToString("O") });
    }

    public void Delete(long id)
    {
        connection.Execute("DELETE FROM GeneratedReport WHERE Id = @id;", new { id });
    }

    // long rather than int/DateTimeOffset directly, matching Dapper's exact-type-match constructor
    // materialization against SQLite's underlying INTEGER/TEXT reader types (see PartRepository.PartRow).
    private sealed record GeneratedReportRow(long Id, long PageId, string ReportKind, long? SourceEntityId,
        string? GroupingKey, string GeneratedAtUtc)
    {
        public GeneratedReport ToModel() => new()
        {
            Id = Id,
            PageId = PageId,
            ReportKind = ReportKind,
            SourceEntityId = SourceEntityId,
            GroupingKey = GroupingKey,
            GeneratedAtUtc = DateTimeOffset.Parse(GeneratedAtUtc),
        };
    }
}
