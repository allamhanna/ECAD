namespace Ecad.App.ViewModels;

/// <summary>Which segment the Pages sidebar currently groups by — "structural pages" are just the
/// group headers this produces, not a new kind of Page.</summary>
public enum PageGroupBy
{
    None,
    Function,
    Location,
    DocumentType,
}

/// <summary>Serialized as-is into Project.PageNavigatorSettingsJson.</summary>
public sealed class PageNavigatorSettings
{
    public PageGroupBy GroupBy { get; set; } = PageGroupBy.None;
}
