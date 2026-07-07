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
    private readonly SchematicPageViewModel _viewModel;

    public SchematicPageWindow(ProjectSession session, Page page)
    {
        InitializeComponent();
        _viewModel = new SchematicPageViewModel(session, page);
        DataContext = _viewModel;
        _viewModel.RedrawRequested += () => SkiaCanvas.InvalidateVisual();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _viewModel.OwnerWindow = this;
    }

    protected override void OnClosed(EventArgs e)
    {
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
            _viewModel.HandleLeftButtonDown(position.X, position.Y);
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
