using Dapper;
using Xunit;

namespace Ecad.Data.Tests;

public class MigrationTests
{
    [Fact]
    public void ProjectDatabase_Open_AppliesAllMigrations()
    {
        using var file = new TempSqliteFile();
        using var connection = ProjectDatabase.Open(file.Path);

        var version = connection.QuerySingle<int>("SELECT MAX(version) FROM schema_migrations;");
        Assert.Equal(9, version);

        var tableExists = connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Connection';");
        Assert.Equal(1, tableExists);

        var partImageExists = connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PartImage';");
        Assert.Equal(1, partImageExists);
    }

    [Fact]
    public void ProjectDatabase_Open_Migration0003_AddsCableProjectIdColumn()
    {
        using var file = new TempSqliteFile();
        using var connection = ProjectDatabase.Open(file.Path);

        var columnExists = connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM pragma_table_info('Cable') WHERE name = 'ProjectId';");
        Assert.Equal(1, columnExists);
    }

    [Fact]
    public void ProjectDatabase_Open_Migration0005_CreatesDefinitionPointTable_AndDropsTheOldConnectionColumn()
    {
        using var file = new TempSqliteFile();
        using var connection = ProjectDatabase.Open(file.Path);

        var tableExists = connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DefinitionPoint';");
        Assert.Equal(1, tableExists);

        var oldColumnExists = connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM pragma_table_info('Connection') WHERE name = 'DefinitionPointPositionT';");
        Assert.Equal(0, oldColumnExists);
    }

    [Fact]
    public void ProjectDatabase_Open_Migration0006_CreatesCableLineAndCrossingTables()
    {
        using var file = new TempSqliteFile();
        using var connection = ProjectDatabase.Open(file.Path);

        var cableLineExists = connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CableLine';");
        Assert.Equal(1, cableLineExists);

        var crossingExists = connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CableLineCrossing';");
        Assert.Equal(1, crossingExists);
    }

    [Fact]
    public void ProjectDatabase_Open_Migration0007_AddsRotationDegreesColumns()
    {
        using var file = new TempSqliteFile();
        using var connection = ProjectDatabase.Open(file.Path);

        var definitionPointColumnExists = connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM pragma_table_info('DefinitionPoint') WHERE name = 'RotationDegrees';");
        Assert.Equal(1, definitionPointColumnExists);

        var crossingColumnExists = connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM pragma_table_info('CableLineCrossing') WHERE name = 'RotationDegrees';");
        Assert.Equal(1, crossingColumnExists);
    }

    [Fact]
    public void ProjectDatabase_Open_Migration0008_CreatesGeneratedReportTable()
    {
        using var file = new TempSqliteFile();
        using var connection = ProjectDatabase.Open(file.Path);

        var tableExists = connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='GeneratedReport';");
        Assert.Equal(1, tableExists);
    }

    [Fact]
    public void ProjectDatabase_Open_Migration0009_AddsPageNavigatorSettingsJsonColumn()
    {
        using var file = new TempSqliteFile();
        using var connection = ProjectDatabase.Open(file.Path);

        var columnExists = connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM pragma_table_info('Project') WHERE name = 'PageNavigatorSettingsJson';");
        Assert.Equal(1, columnExists);
    }

    [Fact]
    public void LibraryDatabase_Open_AppliesAllMigrations()
    {
        using var file = new TempSqliteFile();
        using var connection = LibraryDatabase.Open(file.Path);

        var version = connection.QuerySingle<int>("SELECT MAX(version) FROM schema_migrations;");
        Assert.Equal(2, version);

        var tableExists = connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Part';");
        Assert.Equal(1, tableExists);

        var partImageExists = connection.QuerySingle<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PartImage';");
        Assert.Equal(1, partImageExists);
    }

    [Fact]
    public void ProjectDatabase_Open_IsIdempotent()
    {
        using var file = new TempSqliteFile();
        using (var first = ProjectDatabase.Open(file.Path)) { }
        using var second = ProjectDatabase.Open(file.Path);

        var version = second.QuerySingle<int>("SELECT MAX(version) FROM schema_migrations;");
        Assert.Equal(9, version);
    }
}
