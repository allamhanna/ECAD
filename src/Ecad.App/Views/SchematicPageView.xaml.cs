using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ecad.App.ViewModels;
using Ecad.Rendering.Canvas;
using Ecad.Rendering.Symbols;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Ecad.App.Views;

/// <summary>
/// One open schematic page's canvas, hosted as a tab's content in MainWindow (M10 folds what used
/// to be its own floating SchematicPageWindow into the single-window tabbed shell — MainViewModel's
/// OpenOrFocusPageTab is the new "show me this page" entry point, replacing the old per-page window
/// registry). Hosts an SKElement plus a symbol palette sidebar; all interaction logic lives in
/// SchematicPageViewModel, this code-behind only translates WPF events into calls on it.
/// </summary>
public partial class SchematicPageView : UserControl
{
    // WPF doesn't natively support drag-starting from a Button's own MouseDown (it's busy handling
    // Click) — this tracks where the left button first went down on a palette item so
    // OnPaletteItemPreviewMouseMove can tell an intentional drag from an ordinary click.
    private const double DragStartThreshold = 4;
    private Point? _paletteDragStartPosition;

    public SchematicPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => SkiaCanvas.InvalidateVisual();
    }

    // A View hosted via an implicit DataTemplate (as this one is, inside MainWindow's document
    // TabControl) gets its DataContext assigned AFTER construction, not during it — wiring
    // RedrawRequested in the constructor (as SchematicPageWindow used to) would silently never fire,
    // since there'd be no ViewModel yet to subscribe to. Wire it here instead, on every DataContext
    // change, so the canvas actually repaints. This alone still isn't enough for the FIRST paint,
    // though: nothing in the ViewModel's constructor raises RedrawRequested, and SKElement doesn't
    // reliably self-paint the moment it's newly templated into a freshly-selected tab — a real bug a
    // live click-through found (opening a page's tab showed nothing until a stray canvas click forced
    // a repaint). Force one explicitly here, and again on Loaded (whichever event lands second is the
    // one that actually has a laid-out SKElement to invalidate).
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SchematicPageViewModel oldViewModel) oldViewModel.RedrawRequested -= OnRedrawRequested;
        if (e.NewValue is SchematicPageViewModel newViewModel)
        {
            newViewModel.RedrawRequested += OnRedrawRequested;
            SkiaCanvas.InvalidateVisual();
        }
    }

    private void OnRedrawRequested() => SkiaCanvas.InvalidateVisual();

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (DataContext is not SchematicPageViewModel viewModel) return;
        viewModel.ApplyPendingCenter(e.Info.Width, e.Info.Height);
        SchematicCanvasRenderer.Render(e.Surface.Canvas, viewModel.Viewport, e.Info.Width, e.Info.Height,
            viewModel.BuildRenderList(), viewModel.GetEffectiveSelectedPlacementIds(), viewModel.BuildWiringRenderInfo(),
            viewModel.BuildRubberBandRenderInfo(), viewModel.WireColor);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not SchematicPageViewModel viewModel) return;
        SkiaCanvas.Focus();
        var position = e.GetPosition(SkiaCanvas);

        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            viewModel.HandleDoubleClick(position.X, position.Y);
        }
        else if (e.ChangedButton == MouseButton.Left)
        {
            viewModel.HandleLeftButtonDown(position.X, position.Y, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            // Without capture, WPF stops delivering MouseMove/MouseUp to this element the instant the
            // cursor leaves its bounds — a rubber-band drag or an object drag that strays outside the
            // canvas (easy to do, since the canvas doesn't fill the whole window) would freeze until the
            // cursor wandered back in, then jump. Capturing keeps every move/up routed here regardless
            // of where the cursor physically is, matching EPLAN's own drag behavior.
            SkiaCanvas.CaptureMouse();
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            // Selects whatever's under the cursor (no drag) before the native ContextMenu (declared in
            // XAML, bound to DeleteSelection/RotateSelection/Undo/Redo) opens on the matching MouseUp.
            viewModel.HandleRightClick(position.X, position.Y);
        }
        else if (e.ChangedButton == MouseButton.Middle)
        {
            viewModel.HandlePanStart(position.X, position.Y);
            SkiaCanvas.CaptureMouse();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not SchematicPageViewModel viewModel) return;
        var position = e.GetPosition(SkiaCanvas);
        viewModel.HandleMouseMove(position.X, position.Y);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not SchematicPageViewModel viewModel) return;
        if (e.ChangedButton == MouseButton.Left)
        {
            viewModel.HandleLeftButtonUp();
            SkiaCanvas.ReleaseMouseCapture();
        }
        else if (e.ChangedButton == MouseButton.Middle)
        {
            viewModel.HandlePanEnd();
            SkiaCanvas.ReleaseMouseCapture();
        }
    }

    /// <summary>Capture can be taken away by something outside our control (e.g. focus stolen by
    /// another window mid-drag) — without this, an in-progress drag/pan/rubber-band would be stuck
    /// waiting for a MouseUp that will never arrive. Cancels cleanly instead of leaving it dangling.</summary>
    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (DataContext is SchematicPageViewModel viewModel) viewModel.CancelActiveDrag();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not SchematicPageViewModel viewModel) return;
        var position = e.GetPosition(SkiaCanvas);
        viewModel.HandleMouseWheel(position.X, position.Y, e.Delta);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SchematicPageViewModel viewModel) return;
        var ctrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        viewModel.HandleKeyDown(e.Key, ctrlPressed);
    }

    private void OnPaletteItemPreviewMouseDown(object sender, MouseButtonEventArgs e) =>
        _paletteDragStartPosition = e.GetPosition(null);

    /// <summary>Starts a drag once the mouse has moved far enough from where the button went down
    /// while still held — distinguishes an intentional drag from the ordinary click that
    /// SelectPaletteSymbolCommand already handles (both stay available side by side).</summary>
    private void OnPaletteItemPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_paletteDragStartPosition is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not FrameworkElement { DataContext: PaletteItem item } element) return;

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - start.X) < DragStartThreshold && Math.Abs(current.Y - start.Y) < DragStartThreshold) return;

        _paletteDragStartPosition = null;
        DragDrop.DoDragDrop(element, item.Symbol, DragDropEffects.Copy);
    }

    private void OnCanvasDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(LoadedSymbol)) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCanvasDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not SchematicPageViewModel viewModel) return;
        if (e.Data.GetData(typeof(LoadedSymbol)) is not LoadedSymbol symbol) return;

        var position = e.GetPosition(SkiaCanvas);
        viewModel.PlaceSymbolAtScreenPosition(symbol, position.X, position.Y);
    }
}
