using Dapper;
using Xunit;

namespace Ecad.Data.Tests;

public class MigrationTests
{
    [Fact]
    public void ProjectDatabase_Open_AppliesMigrationsToVersion1()
    {
        using var file = new TempSqliteFile();
        using var connection = ProjectDatabase.Open(file.Path);

        var version = connection.QuerySingle<int>("SELECT MAX(version) FROM schema_migrations;");
        Assert.Equal(1, version);

        var tableExists = connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Connection';");
        Assert.Equal(1, tableExists);
    }

    [Fact]
    public void LibraryDatabase_Open_AppliesMigrationsToVersion1()
    {
        using var file = new TempSqliteFile();
        using var connection = LibraryDatabase.Open(file.Path);

        var version = connection.QuerySingle<int>("SELECT MAX(version) FROM schema_migrations;");
        Assert.Equal(1, version);

        var tableExists = connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Part';");
        Assert.Equal(1, tableExists);
    }

    [Fact]
    public void ProjectDatabase_Open_IsIdempotent()
    {
        using var file = new TempSqliteFile();
        using (var first = ProjectDatabase.Open(file.Path)) { }
        using var second = ProjectDatabase.Open(file.Path);

        var version = second.QuerySingle<int>("SELECT MAX(version) FROM schema_migrations;");
        Assert.Equal(1, version);
    }
}
