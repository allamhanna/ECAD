using System.Text.Json.Serialization;

namespace Ecad.Rendering.Symbols;

/// <summary>
/// A symbol's metadata (connection points, text placeholders, variants), loaded from a
/// "{Name}.symbol.json" sidecar next to a plain "{Name}.svg" file — see DECISIONS.md ADR-006
/// for the format and the shared 0..40 viewBox convention every symbol uses.
/// </summary>
public sealed class SymbolDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("connectionPoints")]
    public List<SymbolConnectionPoint> ConnectionPoints { get; set; } = [];

    [JsonPropertyName("textPlaceholders")]
    public List<SymbolTextPlaceholder> TextPlaceholders { get; set; } = [];

    [JsonPropertyName("variants")]
    public List<SymbolVariant> Variants { get; set; } = [];
}

public sealed class SymbolConnectionPoint
{
    [JsonPropertyName("pin")]
    public string Pin { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    /// <summary>Outward direction of the connection in degrees (0 = right, 90 = down, 180 = left, 270 = up).</summary>
    [JsonPropertyName("direction")]
    public double Direction { get; set; }
}

public sealed class SymbolTextPlaceholder
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("anchor")]
    public string Anchor { get; set; } = "middle";
}

public sealed class SymbolVariant
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Default";

    [JsonPropertyName("rotationDegrees")]
    public int RotationDegrees { get; set; }

    [JsonPropertyName("mirrored")]
    public bool Mirrored { get; set; }
}
