using System.Globalization;
using System.Xml.Linq;
using Ecad.Core.Enums;
using Ecad.Core.Models;
using Ecad.Data.Repositories;
using Microsoft.Data.Sqlite;
using SharpCompress.Archives;

namespace Ecad.Data.Import;

/// <summary>
/// Imports EPLAN's native parts export (a 7z archive conventionally named .edz) into the Library
/// DB. See DECISIONS.md ADR-003/ADR-004 for the format and scope this covers.
///
/// Reads via the generic SharpCompress IArchive/IArchiveEntry interfaces (auto-detects the
/// container format) rather than the 7z-specific type, so tests can exercise this same code path
/// against a plain .zip fixture — SharpCompress can only read 7z, not write it, and a real .edz
/// is too large/proprietary to commit as a test fixture.
/// </summary>
public static class EplanEdzImporter
{
    public static EplanImportResult Import(string edzFilePath, SqliteConnection libraryConnection)
    {
        var result = new EplanImportResult();
        var parts = new PartRepository(libraryConnection);
        var now = DateTimeOffset.UtcNow;

        var batchId = parts.InsertImportBatch(new ImportBatch
        {
            SourceType = ImportSourceType.EplanEdz,
            SourcePath = edzFilePath,
            ImportedAtUtc = now,
        });

        using var archive = ArchiveFactory.OpenArchive(edzFilePath);
        // GroupBy + first-wins rather than ToDictionary: the real H2L Robotics export has at least
        // one duplicate entry key (e.g. two "teejet.manufacturer.xml" entries) — almost certainly
        // redundant/identical source files, not worth failing the whole import over.
        var entriesByPath = archive.Entries
            .Where(e => !e.IsDirectory)
            .GroupBy(e => e.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        if (!entriesByPath.TryGetValue("manifest.xml", out var manifestEntry))
            throw new InvalidOperationException($"'{edzFilePath}' has no manifest.xml — not a recognized EPLAN parts export.");

        var manifest = LoadXml(manifestEntry);

        foreach (var package in manifest.Root!.Element("packages")!.Elements("package"))
        {
            if ((string?)package.Attribute("type") != "part") continue;

            var packageKey = (string?)package.Attribute("key") ?? "(unknown)";
            // First-wins on duplicate item names within one package (seen in real data for macro/
            // picture items we don't read anyway) — only the names we actually look up below matter.
            var items = package.Element("items")!.Elements("item")
                .GroupBy(i => (string)i.Attribute("name")!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            if (!items.TryGetValue("part", out var partItem))
            {
                result.Warnings.Add($"Package '{packageKey}' has no part.xml entry — skipped.");
                continue;
            }

            var partPath = ArchivePath(partItem);
            if (!entriesByPath.TryGetValue(partPath, out var partEntry))
            {
                result.Warnings.Add($"'{partPath}' referenced by manifest but missing from archive — skipped.");
                continue;
            }

            // One malformed part.xml/connectionpoints.xml shouldn't take the whole batch down —
            // real-world exports are messier than the schema assumes (see M3 log entries for the
            // specific issues this uncovered). Everything already committed for prior parts stands;
            // this package is recorded as a warning and the loop moves on.
            try
            {
                var (part, pinTemplates, accessories) = ParsePart(LoadXml(partEntry));

                if (items.TryGetValue("manufacturer", out var manufacturerItem))
                    part.ManufacturerId = ResolveOrganization(manufacturerItem, entriesByPath, parts);
                if (items.TryGetValue("supplier", out var supplierItem))
                    part.SupplierId = ResolveOrganization(supplierItem, entriesByPath, parts);

                part.SourceImportBatchId = batchId;
                var upsertResult = parts.UpsertByExternalKey(part, now);

                switch (upsertResult)
                {
                    case PartUpsertResult.Added: result.PartsAdded++; break;
                    case PartUpsertResult.Updated: result.PartsUpdated++; break;
                    default: result.PartsUnchanged++; break;
                }

                // Image backfill runs regardless of upsert result: parts imported before this
                // feature existed are "Unchanged" on re-import (their source timestamp hasn't
                // moved) and would otherwise never get an image. UpsertImage is idempotent, so
                // running it every time a picturefile item exists is cheap and safe.
                if (items.TryGetValue("picturefile", out var pictureItem))
                    BackfillImage(pictureItem, entriesByPath, parts, part.Id, packageKey, result);

                if (upsertResult == PartUpsertResult.Unchanged) continue; // leave child rows as they are

                parts.ReplacePartPinTemplates(part.Id, pinTemplates);
                parts.ReplacePartAccessories(part.Id, accessories);

                if (items.TryGetValue("connectionpoints", out var connectionPointsItem))
                {
                    var cpPath = ArchivePath(connectionPointsItem);
                    if (entriesByPath.TryGetValue(cpPath, out var cpEntry))
                    {
                        parts.ReplacePartTerminalSpecs(part.Id, ParseConnectionPoints(LoadXml(cpEntry)));
                    }
                    else
                    {
                        result.Warnings.Add($"'{cpPath}' referenced by manifest but missing from archive — terminal specs skipped for '{packageKey}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Package '{packageKey}' failed to import: {ex.Message}");
            }
        }

        parts.UpdateImportBatchCounts(batchId, result.PartsAdded, result.PartsUpdated, result.PartsUnchanged);
        return result;
    }

    /// <summary>
    /// EPLAN's manifest locators are relative to a folder named after the item's type:
    /// items/{type}/{locator}. SharpCompress reports entry keys with forward slashes regardless of
    /// how 7-Zip's own CLI displays them (which uses backslashes) — this must match the former.
    /// </summary>
    private static string ArchivePath(XElement item)
    {
        var type = (string)item.Attribute("type")!;
        var locator = ((string)item.Attribute("locator")!).Replace('\\', '/');
        return $"items/{type}/{locator}";
    }

    private static long? ResolveOrganization(XElement item, Dictionary<string, IArchiveEntry> entriesByPath, PartRepository parts)
    {
        var path = ArchivePath(item);
        if (!entriesByPath.TryGetValue(path, out var entry)) return null;

        var address = LoadXml(entry).Root!.Element("address");
        if (address is null) return null;

        var shortName = (string?)address.Attribute("P_PART_ADDRESS_SHORTNAME") ?? Path.GetFileNameWithoutExtension(path);
        var longName = (string?)address.Attribute("P_PART_ADDRESS_LONGNAME") ?? shortName;
        return parts.GetOrCreateOrganization(longName, shortName);
    }

    private static void BackfillImage(XElement pictureItem, Dictionary<string, IArchiveEntry> entriesByPath,
        PartRepository parts, long partId, string packageKey, EplanImportResult result)
    {
        var path = ArchivePath(pictureItem);
        if (!entriesByPath.TryGetValue(path, out var entry))
        {
            result.Warnings.Add($"'{path}' referenced by manifest but missing from archive — image skipped for '{packageKey}'.");
            return;
        }

        if (parts.GetImage(partId) is not null) return; // already cached — UpsertImage is cheap, but no need to re-read the entry

        try
        {
            using var stream = entry.OpenEntryStream();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            parts.UpsertImage(partId, ContentTypeFromExtension(path), buffer.ToArray());
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Failed to read image '{path}' for '{packageKey}': {ex.Message}");
        }
    }

    private static string ContentTypeFromExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        _ => "image/jpeg", // .jpg/.jpeg and unrecognized extensions — the real export is overwhelmingly JPEG
    };

    private static (Part part, List<PartPinTemplate> pinTemplates, List<PartAccessory> accessories) ParsePart(XDocument xml)
    {
        var partEl = xml.Root!.Element("part")!;

        var part = new Part
        {
            ExternalKey = (string)partEl.Attribute("P_ARTICLE_PARTNR")!,
            TypeNumber = (string?)partEl.Attribute("P_ARTICLE_TYPENR"),
            Description1 = StripLocaleTag((string?)partEl.Attribute("P_ARTICLE_DESCR1")),
            Description2 = StripLocaleTag((string?)partEl.Attribute("P_ARTICLE_DESCR2")),
            Note = StripLocaleTag((string?)partEl.Attribute("P_ARTICLE_NOTE")),
            HeightMm = (double?)partEl.Attribute("P_ARTICLE_HEIGHT"),
            WidthMm = (double?)partEl.Attribute("P_ARTICLE_WIDTH"),
            DepthMm = (double?)partEl.Attribute("P_ARTICLE_DEPTH"),
            WeightKg = (double?)partEl.Attribute("P_ARTICLE_WEIGHT"),
            IsAccessory = (string?)partEl.Attribute("P_ARTICLE_IS_ACCESSORY") == "1",
            PartType = PartType.Other, // see ADR-003/M3 plan: no verified mapping for EPLAN's internal part-type codes
            ErpNumber = (string?)partEl.Attribute("P_ARTICLE_ERPNR"),
            PriceUnit = (int?)partEl.Attribute("P_ARTICLE_PRICEUNIT"),
            SalesPrice1 = (decimal?)partEl.Attribute("P_ARTICLE_SALESPRICE_1"),
            SalesPrice2 = (decimal?)partEl.Attribute("P_ARTICLE_SALESPRICE_2"),
            PurchasePrice1 = (decimal?)partEl.Attribute("P_ARTICLE_PURCHASEPRICE_1"),
            PurchasePrice2 = (decimal?)partEl.Attribute("P_ARTICLE_PURCHASEPRICE_2"),
            SourceLastModifiedUtc = ParseUnixSeconds((string?)partEl.Attribute("P_PART_LASTCHANGE_DATE_UTC")),
        };

        var pinTemplates = new List<PartPinTemplate>();
        var accessories = new List<PartAccessory>();

        // Only the first <variant> is imported in this pass — see M3 plan for the multi-variant caveat.
        var firstVariant = partEl.Elements("variant").FirstOrDefault();
        if (firstVariant is not null)
        {
            foreach (var functionTemplate in firstVariant.Elements("functiontemplate"))
            {
                pinTemplates.Add(new PartPinTemplate
                {
                    Pos = (int)functionTemplate.Attribute("pos")!,
                    ConnectionDesignation = ((string?)functionTemplate.Attribute("connectionDesignation"))?.Replace("\n", " / "),
                    FunctionDefCategory = (int?)functionTemplate.Attribute("functiondefcategory"),
                    FunctionDefGroup = (int?)functionTemplate.Attribute("functiondefgroup"),
                    FunctionDefId = (int?)functionTemplate.Attribute("functiondefid"),
                    SymbolRef = (string?)functionTemplate.Attribute("symbol"),
                });
            }

            foreach (var assemblyPosition in firstVariant.Elements("assemblyposition"))
            {
                accessories.Add(new PartAccessory
                {
                    AccessoryPartExternalKey = (string)assemblyPosition.Attribute("partnr")!,
                    Pos = (int)assemblyPosition.Attribute("pos")!,
                });
            }
        }

        return (part, pinTemplates, accessories);
    }

    private static List<PartTerminalSpec> ParseConnectionPoints(XDocument xml)
    {
        var terminalEl = xml.Root!.Element("terminal");
        if (terminalEl is null) return [];

        return terminalEl.Elements("terminalPosition").Select(terminalPosition => new PartTerminalSpec
        {
            // Real data has terminalPosition elements with no name attribute — fall back to the
            // position number rather than write a NULL into a NOT NULL column.
            Name = (string?)terminalPosition.Attribute("name") ?? $"#{(int?)terminalPosition.Attribute("pos") ?? 0}",
            Pos = (int)terminalPosition.Attribute("pos")!,
            MinCrossSectionMm2 = (double?)terminalPosition.Attribute("mincrosssection"),
            MaxCrossSectionMm2 = (double?)terminalPosition.Attribute("maxcrosssection"),
            MinTorqueNm = (double?)terminalPosition.Attribute("mintorque"),
            MaxTorqueNm = (double?)terminalPosition.Attribute("maxtorque"),
            MaxWireCount = (int?)terminalPosition.Attribute("maxwirecount"),
            X = (double?)terminalPosition.Attribute("xpos") ?? 0,
            Y = (double?)terminalPosition.Attribute("ypos") ?? 0,
            Z = (double?)terminalPosition.Attribute("zpos") ?? 0,
        }).ToList();
    }

    /// <summary>Strips EPLAN's language-tagged string wrapper, e.g. "de_DE@some text;" -&gt; "some text".</summary>
    private static string? StripLocaleTag(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        var text = raw;
        if (text.Length > 6 && text[2] == '_' && text[5] == '@')
            text = text[6..];
        if (text.EndsWith(';'))
            text = text[..^1];

        return text.Length == 0 ? null : text;
    }

    private static DateTimeOffset? ParseUnixSeconds(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    private static XDocument LoadXml(IArchiveEntry entry)
    {
        using var stream = entry.OpenEntryStream();
        return XDocument.Load(stream);
    }
}
