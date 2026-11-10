using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace ZeroFileCommander.Controls;

/// <summary>
/// Paneller arasındaki dikey ayırıcı. Sürükleyince yatay farkı bildirir;
/// üzerine gelince ince bir çizgi belirir, imleç ↔ olur.
/// </summary>
public sealed partial class PaneSplitter : Grid
{
    private readonly Rectangle _line;
    private bool _dragging;
    private double _lastX;

    /// <summary>Sürükleme sırasında piksel cinsinden yatay fark.</summary>
    public event EventHandler<double>? Dragged;

    /// <summary>Bırakıldığında (kaydetmek için).</summary>
    public event EventHandler? DragCompleted;

    /// <summary>Çift tıklayınca (varsayılana döndürmek için).</summary>
    public event EventHandler? ResetRequested;

    public PaneSplitter()
    {
        Width = 8;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

        _line = new Rectangle
        {
            Width = 2,
            RadiusX = 1,
            RadiusY = 1,
            Margin = new Thickness(0, 24, 0, 24),
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0,
        };
        Loaded += (_, _) =>
        {
            if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var brush) && brush is Brush b)
                _line.Fill = b;
        };
        Children.Add(_line);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, "Panel ayırıcı");
    }

    protected override void OnPointerEntered(PointerRoutedEventArgs e)
    {
        base.OnPointerEntered(e);
        _line.Opacity = 0.8;
    }

    protected override void OnPointerExited(PointerRoutedEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_dragging) _line.Opacity = 0;
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = CapturePointer(e.Pointer);
        _lastX = e.GetCurrentPoint(null).Position.X;
        _line.Opacity = 1;
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        double x = e.GetCurrentPoint(null).Position.X;
        double delta = x - _lastX;
        if (Math.Abs(delta) < 0.5) return;
        _lastX = x;
        Dragged?.Invoke(this, delta);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndDrag(e.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag(null);
    }

    protected override void OnDoubleTapped(DoubleTappedRoutedEventArgs e)
    {
        base.OnDoubleTapped(e);
        ResetRequested?.Invoke(this, EventArgs.Empty);
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void EndDrag(Pointer? pointer)
    {
        if (!_dragging) return;
        _dragging = false;
        if (pointer is not null) ReleasePointerCapture(pointer);
        _line.Opacity = 0;
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }
}
