using Ecad.Core.Enums;

namespace Ecad.Core.Models;

public class Page
{
    public long Id { get; set; }
    public long ProjectId { get; set; }

    public string? FunctionSegment { get; set; }
    public string? LocationSegment { get; set; }
    public string? DocumentTypeSegment { get; set; }
    public string? PageNumberSegment { get; set; }

    public PageType PageType { get; set; } = PageType.Schematic;
    public string FrameFormat { get; set; } = "A3";
    public int SortOrder { get; set; }
}
