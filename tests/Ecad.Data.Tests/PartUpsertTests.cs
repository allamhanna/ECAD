using Ecad.Core.Models;
using Ecad.Data.Repositories;
using Xunit;

namespace Ecad.Data.Tests;

public class PartUpsertTests
{
    [Fact]
    public void UpsertByExternalKey_NewKey_Adds()
    {
        using var file = new TempSqliteFile();
        using var connection = LibraryDatabase.Open(file.Path);
        var parts = new PartRepository(connection);

        var result = parts.UpsertByExternalKey(
            new Part { ExternalKey = "3RT2015-1BB41+1FA22", Description1 = "Contactor", SourceLastModifiedUtc = DateTimeOffset.Parse("2018-07-11T11:25:35Z") },
            DateTimeOffset.UtcNow);

        Assert.Equal(PartUpsertResult.Added, result);
        Assert.NotNull(parts.GetPartByExternalKey("3RT2015-1BB41+1FA22"));
    }

    [Fact]
    public void UpsertByExternalKey_NewerSourceTimestamp_Updates()
    {
        using var file = new TempSqliteFile();
        using var connection = LibraryDatabase.Open(file.Path);
        var parts = new PartRepository(connection);

        parts.UpsertByExternalKey(
            new Part { ExternalKey = "K1", Description1 = "Old description", SourceLastModifiedUtc = DateTimeOffset.Parse("2020-01-01T00:00:00Z") },
            DateTimeOffset.UtcNow);

        var result = parts.UpsertByExternalKey(
            new Part { ExternalKey = "K1", Description1 = "New description", SourceLastModifiedUtc = DateTimeOffset.Parse("2021-01-01T00:00:00Z") },
            DateTimeOffset.UtcNow);

        Assert.Equal(PartUpsertResult.Updated, result);
        Assert.Equal("New description", parts.GetPartByExternalKey("K1")!.Description1);
    }

    [Fact]
    public void UpsertByExternalKey_OlderOrEqualSourceTimestamp_IsNoOp()
    {
        using var file = new TempSqliteFile();
        using var connection = LibraryDatabase.Open(file.Path);
        var parts = new PartRepository(connection);

        var sameTimestamp = DateTimeOffset.Parse("2021-01-01T00:00:00Z");
        parts.UpsertByExternalKey(
            new Part { ExternalKey = "K1", Description1 = "Original", SourceLastModifiedUtc = sameTimestamp },
            DateTimeOffset.UtcNow);

        var equalResult = parts.UpsertByExternalKey(
            new Part { ExternalKey = "K1", Description1 = "Should not apply", SourceLastModifiedUtc = sameTimestamp },
            DateTimeOffset.UtcNow);
        var olderResult = parts.UpsertByExternalKey(
            new Part { ExternalKey = "K1", Description1 = "Should also not apply", SourceLastModifiedUtc = sameTimestamp.AddDays(-1) },
            DateTimeOffset.UtcNow);

        Assert.Equal(PartUpsertResult.Unchanged, equalResult);
        Assert.Equal(PartUpsertResult.Unchanged, olderResult);
        Assert.Equal("Original", parts.GetPartByExternalKey("K1")!.Description1);
    }

    [Fact]
    public void PartPinTemplatesAndTerminalSpecs_RoundTrip()
    {
        using var file = new TempSqliteFile();
        using var connection = LibraryDatabase.Open(file.Path);
        var parts = new PartRepository(connection);

        parts.UpsertByExternalKey(new Part { ExternalKey = "K1" }, DateTimeOffset.UtcNow);
        var partId = parts.GetPartByExternalKey("K1")!.Id;

        parts.InsertPartPinTemplate(new PartPinTemplate { PartId = partId, Pos = 1, ConnectionDesignation = "A1\nA2" });
        parts.InsertPartTerminalSpec(new PartTerminalSpec { PartId = partId, Name = "A1", Pos = 1, MinCrossSectionMm2 = 1, MaxCrossSectionMm2 = 2.5 });

        Assert.Single(parts.GetPartPinTemplates(partId));
        Assert.Single(parts.GetPartTerminalSpecs(partId));
    }
}
