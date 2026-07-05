using Ecad.Core.Enums;

namespace Ecad.Core.Models;

/// <summary>
/// A parts-library article. Identical schema in the Library DB (master) and each Project DB
/// (a local cache/copy, populated when a Device first references the part, so the project
/// file stays portable without needing the shared library DB).
/// </summary>
public class Part
{
    public long Id { get; set; }

    /// <summary>The EPLAN (or other source) article key, e.g. P_ARTICLE_PARTNR. Unique within a database.</summary>
    public string ExternalKey { get; set; } = string.Empty;

    public string? TypeNumber { get; set; }
    public string? Description1 { get; set; }
    public string? Description2 { get; set; }
    public long? ManufacturerId { get; set; }
    public long? SupplierId { get; set; }
    public long? ClassificationId { get; set; }

    public double? HeightMm { get; set; }
    public double? WidthMm { get; set; }
    public double? DepthMm { get; set; }
    public double? WeightKg { get; set; }

    public PartType PartType { get; set; } = PartType.Other;
    public bool IsAccessory { get; set; }

    public int? PriceUnit { get; set; }
    public decimal? SalesPrice1 { get; set; }
    public decimal? SalesPrice2 { get; set; }
    public decimal? PurchasePrice1 { get; set; }
    public decimal? PurchasePrice2 { get; set; }

    public string? PictureFilePath { get; set; }
    public string? ErpNumber { get; set; }
    public string? Note { get; set; }

    /// <summary>Source system's own last-modified timestamp (e.g. EPLAN's P_PART_LASTCHANGE_DATE_UTC), used to decide update-vs-skip on re-import.</summary>
    public DateTimeOffset? SourceLastModifiedUtc { get; set; }
    public long? SourceImportBatchId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
