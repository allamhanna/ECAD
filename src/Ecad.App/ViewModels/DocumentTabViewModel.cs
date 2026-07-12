namespace Ecad.App.ViewModels;

/// <summary>
/// Wraps a per-document ViewModel (SchematicPageViewModel, GridEditorViewModel, ...) as one tab in
/// MainWindow's document TabControl (M10: replaces the old one-floating-Window-per-document model).
/// Content's concrete type is resolved to its View via implicit DataTemplates declared in
/// MainWindow.xaml. IsProjectScoped controls whether CloseCurrentSession closes this tab too
/// (schematic pages, Grid Editor) or leaves it open (Parts Library, Symbol Browser — already
/// project-independent today).
/// </summary>
public sealed class DocumentTabViewModel
{
    public required string Header { get; init; }
    public required object Content { get; init; }
    public bool IsProjectScoped { get; init; } = true;

    /// <summary>Set only for schematic-page tabs — the find-existing-tab key for OpenOrFocusPageTab.</summary>
    public long? PageId { get; init; }
}
