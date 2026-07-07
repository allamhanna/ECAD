using System.Windows;
using System.Windows.Input;
using Ecad.App.ViewModels;
using Ecad.Core.Models;
using Ecad.Data;
using Ecad.Rendering.Canvas;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Ecad.App.Views;

/// <summary>
/// Non-modal schematic canvas for one page — opened by double-clicking a page in MainWindow.
/// Hosts an SKElement (first real interactive use of SkiaSharp.Views.WPF; M4 only ever rasterized
/// static thumbnails) plus a symbol palette sidebar. All interaction logic lives in
/// SchematicPageViewModel — this code-behind only translates WPF events into calls on it.
/// </summary>
public partial class SchematicPageWindow : Window
{
    // Single-project-at-a-time (M2) makes a plain static registry safe — no per-session scoping
    // needed. Every constructor call self-registers; OnClosed unregisters. Keyed by Page.Id so a
    // page never has more than one window open at once.
    private static readonly Dictionary<long, SchematicPageWindow> OpenWindowsByPageId = new();

    private readonly ProjectSession _session;
    private readonly long _pageId;
    private readonly SchematicPageViewModel _viewModel;

    public SchematicPageWindow(ProjectSession session, Page page, long? focusPlacementId = null)
    {
        InitializeComponent();
        _session = session;
        _pageId = page.Id;
        _viewModel = new SchematicPageViewModel(session, page, focusPlacementId);
        DataContext = _viewModel;
        _viewModel.RedrawRequested += () => SkiaCanvas.InvalidateVisual();
        _viewModel.NavigateToPageRequested += OnNavigateToPageRequested;

        OpenWindowsByPageId[_pageId] = this;
    }

    /// <summary>
    /// Opens the given page, or — if it's already open — brings that window to the front and selects
    /// focusPlacementId instead of opening a duplicate. The single entry point for "show me this
    /// page": both MainWindow's double-click-a-page and Ctrl+Click navigation (below) go through this.
    /// </summary>
    public static void OpenOrFocus(ProjectSession session, Page page, long? focusPlacementId = null, Window? owner = null)
    {
        if (OpenWindowsByPageId.TryGetValue(page.Id, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            if (focusPlacementId is { } id) existing._viewModel.FocusPlacement(id);
            return;
        }

        new SchematicPageWindow(session, page, focusPlacementId) { Owner = owner }.Show();
    }

    /// <summary>Ctrl+Click on a placement with a sibling elsewhere (e.g. an interruption-point pair,
    /// or any multi-placement device) jumps to that page, focusing the existing window for it if one's
    /// already open rather than opening a duplicate.</summary>
    private void OnNavigateToPageRequested(long pageId, long placementId)
    {
        var page = _session.Pages.FirstOrDefault(p => p.Id == pageId);
        if (page is null) return;

        OpenOrFocus(_session, page, placementId, Owner);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _viewModel.OwnerWindow = this;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (OpenWindowsByPageId.TryGetValue(_pageId, out var current) && current == this)
            OpenWindowsByPageId.Remove(_pageId);

        _viewModel.Dispose();
        base.OnClosed(e);
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        SchematicCanvasRenderer.Render(e.Surface.Canvas, _viewModel.Viewport, e.Info.Width, e.Info.Height,
            _viewModel.BuildRenderList(), _viewModel.SelectedPlacementId, _viewModel.BuildWiringRenderInfo());
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        SkiaCanvas.Focus();
        var position = e.GetPosition(SkiaCanvas);

        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            _viewModel.HandleDoubleClick(position.X, position.Y);
        }
        else if (e.ChangedButton == MouseButton.Left)
        {
            _viewModel.HandleLeftButtonDown(position.X, position.Y, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            _viewModel.HandleRightButtonDown(position.X, position.Y);
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(SkiaCanvas);
        _viewModel.HandleMouseMove(position.X, position.Y);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _viewModel.HandleLeftButtonUp();
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            _viewModel.HandleRightButtonUp();
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var position = e.GetPosition(SkiaCanvas);
        _viewModel.HandleMouseWheel(position.X, position.Y, e.Delta);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var ctrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        _viewModel.HandleKeyDown(e.Key, ctrlPressed);
    }
}
