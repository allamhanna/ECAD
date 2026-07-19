using Ecad.Core.Enums;
using Ecad.Core.Models;
using Xunit;

namespace Ecad.Data.Tests;

public class ProjectSessionTests
{
    [Fact]
    public void Create_WritesProjectRowAndFile()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test Machine", CreatedAtUtc = DateTimeOffset.UtcNow });

        Assert.True(File.Exists(file.Path));
        Assert.True(session.CurrentProject.Id > 0);
        Assert.Equal("Test Machine", session.CurrentProject.Name);
        Assert.Empty(session.Pages);
    }

    [Fact]
    public void AddPage_ThenReopen_PagePersists()
    {
        using var file = new TempSqliteFile();
        long projectId, pageId;

        using (var session = ProjectSession.Create(file.Path, new Project { Name = "Test Machine", CreatedAtUtc = DateTimeOffset.UtcNow }))
        {
            var page = session.AddPage(new Page { FunctionSegment = "K1", PageNumberSegment = "5", PageType = PageType.Schematic });
            projectId = session.CurrentProject.Id;
            pageId = page.Id;
        }

        using var reopened = ProjectSession.Open(file.Path);
        Assert.Equal(projectId, reopened.CurrentProject.Id);
        var reopenedPage = Assert.Single(reopened.Pages);
        Assert.Equal(pageId, reopenedPage.Id);
        Assert.Equal("K1", reopenedPage.FunctionSegment);
        Assert.Equal(PageType.Schematic, reopenedPage.PageType);
    }

    [Fact]
    public void Open_MissingProjectRow_Throws()
    {
        using var file = new TempSqliteFile();
        using (ProjectDatabase.Open(file.Path)) { } // creates the schema but no Project row

        Assert.Throws<InvalidOperationException>(() => ProjectSession.Open(file.Path));
    }

    [Fact]
    public void SaveAs_CopiesDataAndFurtherEditsGoToTheNewFileOnly()
    {
        using var original = new TempSqliteFile();
        using var copy = new TempSqliteFile();

        var session = ProjectSession.Create(original.Path, new Project { Name = "Test Machine", CreatedAtUtc = DateTimeOffset.UtcNow });
        session.AddPage(new Page { FunctionSegment = "K1", PageNumberSegment = "5", PageType = PageType.Schematic });

        session = session.SaveAs(copy.Path);
        Assert.Equal(copy.Path, session.FilePath);
        Assert.Single(session.Pages);

        session.AddPage(new Page { FunctionSegment = "K2", PageNumberSegment = "6", PageType = PageType.Schematic });
        session.Dispose();

        using var reopenedCopy = ProjectSession.Open(copy.Path);
        Assert.Equal(2, reopenedCopy.Pages.Count);

        using var reopenedOriginal = ProjectSession.Open(original.Path);
        Assert.Single(reopenedOriginal.Pages); // the post-SaveAs page must not have leaked back into the original file
    }

    [Fact]
    public void SaveAs_SamePath_IsANoOp()
    {
        using var file = new TempSqliteFile();
        using var session = ProjectSession.Create(file.Path, new Project { Name = "Test Machine", CreatedAtUtc = DateTimeOffset.UtcNow });

        var result = session.SaveAs(file.Path);

        Assert.Same(session, result);
    }
}
