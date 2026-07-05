using Dapper;
using Ecad.Core.Enums;
using Ecad.Core.Models;
using Microsoft.Data.Sqlite;

namespace Ecad.Data.Repositories;

/// <summary>Works against either the Library DB or a Project DB's cached copy — the Part-family schema is identical in both.</summary>
public class PartRepository(SqliteConnection connection)
{
    public long InsertOrganization(Organization organization)
    {
        return connection.ExecuteScalar<long>(
            "INSERT INTO Organization (Name, ExternalKey) VALUES (@Name, @ExternalKey) RETURNING Id;",
            organization);
    }

    public long InsertClassification(Classification classification)
    {
        return connection.ExecuteScalar<long>(
            "INSERT INTO Classification (ParentId, Name) VALUES (@ParentId, @Name) RETURNING Id;",
            classification);
    }

    /// <summary>Looks up an Organization by ExternalKey, inserting it if missing. Many parts share the same manufacturer.</summary>
    public long GetOrCreateOrganization(string name, string externalKey)
    {
        var existingId = connection.QuerySingleOrDefault<long?>(
            "SELECT Id FROM Organization WHERE ExternalKey = @externalKey;", new { externalKey });
        if (existingId is not null) return existingId.Value;

        return InsertOrganization(new Organization { Name = name, ExternalKey = externalKey });
    }

    public IReadOnlyList<Organization> GetAllOrganizations()
    {
        return connection.Query<Organization>("SELECT * FROM Organization ORDER BY Name;").ToList();
    }

    public Part? GetPart(long id)
    {
        return connection.QuerySingleOrDefault<PartRow>("SELECT * FROM Part WHERE Id = @id;", new { id })?.ToModel();
    }

    public IReadOnlyList<Part> GetAllParts()
    {
        return connection.Query<PartRow>("SELECT * FROM Part ORDER BY ExternalKey;").Select(r => r.ToModel()).ToList();
    }

    public Part? GetPartByExternalKey(string externalKey)
    {
        return connection.QuerySingleOrDefault<PartRow>(
            "SELECT * FROM Part WHERE ExternalKey = @externalKey;", new { externalKey })?.ToModel();
    }

    /// <summary>
    /// Inserts a new Part, or updates an existing one (matched by ExternalKey) only if the incoming
    /// SourceLastModifiedUtc is strictly newer than what's stored. Equal/older timestamps are a no-op.
    /// This is the mechanism a re-import (M3) uses to decide add vs. update vs. skip.
    /// </summary>
    public PartUpsertResult UpsertByExternalKey(Part part, DateTimeOffset nowUtc)
    {
        var existing = GetPartByExternalKey(part.ExternalKey);
        if (existing is null)
        {
            part.CreatedAtUtc = nowUtc;
            part.UpdatedAtUtc = nowUtc;
            part.Id = connection.ExecuteScalar<long>(
                """
                INSERT INTO Part (ExternalKey, TypeNumber, Description1, Description2, ManufacturerId, SupplierId,
                                   ClassificationId, HeightMm, WidthMm, DepthMm, WeightKg, PartType, IsAccessory,
                                   PriceUnit, SalesPrice1, SalesPrice2, PurchasePrice1, PurchasePrice2,
                                   PictureFilePath, ErpNumber, Note, SourceLastModifiedUtc, SourceImportBatchId,
                                   CreatedAtUtc, UpdatedAtUtc)
                VALUES (@ExternalKey, @TypeNumber, @Description1, @Description2, @ManufacturerId, @SupplierId,
                        @ClassificationId, @HeightMm, @WidthMm, @DepthMm, @WeightKg, @PartTypeValue, @IsAccessory,
                        @PriceUnit, @SalesPrice1, @SalesPrice2, @PurchasePrice1, @PurchasePrice2,
                        @PictureFilePath, @ErpNumber, @Note, @SourceLastModifiedUtcValue, @SourceImportBatchId,
                        @CreatedAtUtcValue, @UpdatedAtUtcValue)
                RETURNING Id;
                """,
                ToParams(part));
            return PartUpsertResult.Added;
        }

        if (part.SourceLastModifiedUtc is not null && existing.SourceLastModifiedUtc is not null
            && part.SourceLastModifiedUtc <= existing.SourceLastModifiedUtc)
        {
            // Callers rely on part.Id being populated after any upsert result, not just Added/Updated
            // (e.g. EplanEdzImporter's image backfill runs on Unchanged parts too) — a real bug this
            // caught: part.Id was left at 0 here, silently writing child rows against the wrong part.
            part.Id = existing.Id;
            return PartUpsertResult.Unchanged;
        }

        part.Id = existing.Id;
        part.CreatedAtUtc = existing.CreatedAtUtc;
        part.UpdatedAtUtc = nowUtc;
        connection.Execute(
            """
            UPDATE Part SET TypeNumber = @TypeNumber, Description1 = @Description1, Description2 = @Description2,
                   ManufacturerId = @ManufacturerId, SupplierId = @SupplierId, ClassificationId = @ClassificationId,
                   HeightMm = @HeightMm, WidthMm = @WidthMm, DepthMm = @DepthMm, WeightKg = @WeightKg,
                   PartType = @PartTypeValue, IsAccessory = @IsAccessory, PriceUnit = @PriceUnit,
                   SalesPrice1 = @SalesPrice1, SalesPrice2 = @SalesPrice2, PurchasePrice1 = @PurchasePrice1,
                   PurchasePrice2 = @PurchasePrice2, PictureFilePath = @PictureFilePath, ErpNumber = @ErpNumber,
                   Note = @Note, SourceLastModifiedUtc = @SourceLastModifiedUtcValue,
                   SourceImportBatchId = @SourceImportBatchId, UpdatedAtUtc = @UpdatedAtUtcValue
            WHERE Id = @Id;
            """,
            ToParams(part));
        return PartUpsertResult.Updated;
    }

    private static object ToParams(Part part) => new
    {
        part.Id,
        part.ExternalKey,
        part.TypeNumber,
        part.Description1,
        part.Description2,
        part.ManufacturerId,
        part.SupplierId,
        part.ClassificationId,
        part.HeightMm,
        part.WidthMm,
        part.DepthMm,
        part.WeightKg,
        PartTypeValue = (int)part.PartType,
        part.IsAccessory,
        part.PriceUnit,
        part.SalesPrice1,
        part.SalesPrice2,
        part.PurchasePrice1,
        part.PurchasePrice2,
        part.PictureFilePath,
        part.ErpNumber,
        part.Note,
        SourceLastModifiedUtcValue = part.SourceLastModifiedUtc?.ToString("O"),
        part.SourceImportBatchId,
        CreatedAtUtcValue = part.CreatedAtUtc.ToString("O"),
        UpdatedAtUtcValue = part.UpdatedAtUtc.ToString("O"),
    };

    public long InsertPartPinTemplate(PartPinTemplate template)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO PartPinTemplate (PartId, Pos, ConnectionDesignation, FunctionDefCategory, FunctionDefGroup, FunctionDefId, SymbolRef)
            VALUES (@PartId, @Pos, @ConnectionDesignation, @FunctionDefCategory, @FunctionDefGroup, @FunctionDefId, @SymbolRef)
            RETURNING Id;
            """,
            template);
    }

    public IReadOnlyList<PartPinTemplate> GetPartPinTemplates(long partId)
    {
        return connection.Query<PartPinTemplate>(
            "SELECT * FROM PartPinTemplate WHERE PartId = @partId ORDER BY Pos;", new { partId }).ToList();
    }

    /// <summary>Deletes all existing pin templates for the part and inserts the given set, in one transaction — used on re-import so rows aren't duplicated.</summary>
    public void ReplacePartPinTemplates(long partId, IReadOnlyList<PartPinTemplate> templates)
    {
        using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM PartPinTemplate WHERE PartId = @partId;", new { partId }, transaction);
        foreach (var template in templates)
        {
            template.PartId = partId;
            connection.Execute(
                """
                INSERT INTO PartPinTemplate (PartId, Pos, ConnectionDesignation, FunctionDefCategory, FunctionDefGroup, FunctionDefId, SymbolRef)
                VALUES (@PartId, @Pos, @ConnectionDesignation, @FunctionDefCategory, @FunctionDefGroup, @FunctionDefId, @SymbolRef);
                """,
                template, transaction);
        }
        transaction.Commit();
    }

    public long InsertPartTerminalSpec(PartTerminalSpec spec)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO PartTerminalSpec (PartId, Name, Pos, MinCrossSectionMm2, MaxCrossSectionMm2, MinTorqueNm, MaxTorqueNm, MaxWireCount, X, Y, Z)
            VALUES (@PartId, @Name, @Pos, @MinCrossSectionMm2, @MaxCrossSectionMm2, @MinTorqueNm, @MaxTorqueNm, @MaxWireCount, @X, @Y, @Z)
            RETURNING Id;
            """,
            spec);
    }

    public IReadOnlyList<PartTerminalSpec> GetPartTerminalSpecs(long partId)
    {
        return connection.Query<PartTerminalSpec>(
            "SELECT * FROM PartTerminalSpec WHERE PartId = @partId ORDER BY Pos;", new { partId }).ToList();
    }

    /// <summary>Deletes all existing terminal specs for the part and inserts the given set, in one transaction — used on re-import so rows aren't duplicated.</summary>
    public void ReplacePartTerminalSpecs(long partId, IReadOnlyList<PartTerminalSpec> specs)
    {
        using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM PartTerminalSpec WHERE PartId = @partId;", new { partId }, transaction);
        foreach (var spec in specs)
        {
            spec.PartId = partId;
            connection.Execute(
                """
                INSERT INTO PartTerminalSpec (PartId, Name, Pos, MinCrossSectionMm2, MaxCrossSectionMm2, MinTorqueNm, MaxTorqueNm, MaxWireCount, X, Y, Z)
                VALUES (@PartId, @Name, @Pos, @MinCrossSectionMm2, @MaxCrossSectionMm2, @MinTorqueNm, @MaxTorqueNm, @MaxWireCount, @X, @Y, @Z);
                """,
                spec, transaction);
        }
        transaction.Commit();
    }

    public IReadOnlyList<PartAccessory> GetPartAccessories(long partId)
    {
        return connection.Query<PartAccessory>(
            "SELECT * FROM PartAccessory WHERE PartId = @partId ORDER BY Pos;", new { partId }).ToList();
    }

    /// <summary>Deletes all existing accessory rows for the part and inserts the given set, in one transaction — used on re-import so rows aren't duplicated.</summary>
    public void ReplacePartAccessories(long partId, IReadOnlyList<PartAccessory> accessories)
    {
        using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM PartAccessory WHERE PartId = @partId;", new { partId }, transaction);
        foreach (var accessory in accessories)
        {
            accessory.PartId = partId;
            connection.Execute(
                "INSERT INTO PartAccessory (PartId, AccessoryPartExternalKey, Pos) VALUES (@PartId, @AccessoryPartExternalKey, @Pos);",
                accessory, transaction);
        }
        transaction.Commit();
    }

    public PartImage? GetImage(long partId)
    {
        return connection.QuerySingleOrDefault<PartImage>("SELECT * FROM PartImage WHERE PartId = @partId;", new { partId });
    }

    /// <summary>Deletes any existing image for the part and inserts the new one — idempotent, used both on first import and on backfilling an existing part.</summary>
    public void UpsertImage(long partId, string contentType, byte[] imageData)
    {
        using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM PartImage WHERE PartId = @partId;", new { partId }, transaction);
        connection.Execute(
            "INSERT INTO PartImage (PartId, ContentType, ImageData) VALUES (@partId, @contentType, @imageData);",
            new { partId, contentType, imageData }, transaction);
        transaction.Commit();
    }

    public long InsertImportBatch(ImportBatch batch)
    {
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO ImportBatch (SourceType, SourcePath, ImportedAtUtc, PartsAdded, PartsUpdated, PartsUnchanged)
            VALUES (@SourceTypeValue, @SourcePath, @ImportedAtUtcValue, @PartsAdded, @PartsUpdated, @PartsUnchanged)
            RETURNING Id;
            """,
            new
            {
                SourceTypeValue = (int)batch.SourceType,
                batch.SourcePath,
                ImportedAtUtcValue = batch.ImportedAtUtc.ToString("O"),
                batch.PartsAdded,
                batch.PartsUpdated,
                batch.PartsUnchanged,
            });
    }

    public void UpdateImportBatchCounts(long importBatchId, int partsAdded, int partsUpdated, int partsUnchanged)
    {
        connection.Execute(
            "UPDATE ImportBatch SET PartsAdded = @partsAdded, PartsUpdated = @partsUpdated, PartsUnchanged = @partsUnchanged WHERE Id = @importBatchId;",
            new { importBatchId, partsAdded, partsUpdated, partsUnchanged });
    }

    // Note: fields matching SQLite's underlying storage types (long for INTEGER, double for REAL) —
    // Dapper's constructor-based record materialization needs an exact match to the reader's field
    // type (unlike its lenient property-setter mapping); bool/enum/decimal conversion happens in ToModel().
    private sealed record PartRow(
        long Id, string ExternalKey, string? TypeNumber, string? Description1, string? Description2,
        long? ManufacturerId, long? SupplierId, long? ClassificationId, double? HeightMm, double? WidthMm,
        double? DepthMm, double? WeightKg, long PartType, long IsAccessory, long? PriceUnit, double? SalesPrice1,
        double? SalesPrice2, double? PurchasePrice1, double? PurchasePrice2, string? PictureFilePath,
        string? ErpNumber, string? Note, string? SourceLastModifiedUtc, long? SourceImportBatchId,
        string CreatedAtUtc, string UpdatedAtUtc)
    {
        public Part ToModel() => new()
        {
            Id = Id,
            ExternalKey = ExternalKey,
            TypeNumber = TypeNumber,
            Description1 = Description1,
            Description2 = Description2,
            ManufacturerId = ManufacturerId,
            SupplierId = SupplierId,
            ClassificationId = ClassificationId,
            HeightMm = HeightMm,
            WidthMm = WidthMm,
            DepthMm = DepthMm,
            WeightKg = WeightKg,
            PartType = (PartType)(int)PartType,
            IsAccessory = IsAccessory != 0,
            PriceUnit = (int?)PriceUnit,
            SalesPrice1 = (decimal?)SalesPrice1,
            SalesPrice2 = (decimal?)SalesPrice2,
            PurchasePrice1 = (decimal?)PurchasePrice1,
            PurchasePrice2 = (decimal?)PurchasePrice2,
            PictureFilePath = PictureFilePath,
            ErpNumber = ErpNumber,
            Note = Note,
            SourceLastModifiedUtc = SourceLastModifiedUtc is null ? null : DateTimeOffset.Parse(SourceLastModifiedUtc),
            SourceImportBatchId = SourceImportBatchId,
            CreatedAtUtc = DateTimeOffset.Parse(CreatedAtUtc),
            UpdatedAtUtc = DateTimeOffset.Parse(UpdatedAtUtc),
        };
    }
}

public enum PartUpsertResult
{
    Added,
    Updated,
    Unchanged,
}
