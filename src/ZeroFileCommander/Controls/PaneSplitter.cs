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

        PointerEntered += HandlePointerEntered;
        PointerExited += HandlePointerExited;
        PointerPressed += HandlePointerPressed;
        PointerMoved += HandlePointerMoved;
        PointerReleased += HandlePointerReleased;
        PointerCaptureLost += HandlePointerCaptureLost;
        DoubleTapped += HandleDoubleTapped;
    }

    private void HandlePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _line.Opacity = 0.8;
    }

    private void HandlePointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) _line.Opacity = 0;
    }

    private void HandlePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = CapturePointer(e.Pointer);
        _lastX = e.GetCurrentPoint(null).Position.X;
        _line.Opacity = 1;
        e.Handled = true;
    }

    private void HandlePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        double x = e.GetCurrentPoint(null).Position.X;
        double delta = x - _lastX;
        if (Math.Abs(delta) < 0.5) return;
        _lastX = x;
        Dragged?.Invoke(this, delta);
        e.Handled = true;
    }

    private void HandlePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndDrag(e.Pointer);
    }

    private void HandlePointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        EndDrag(null);
    }

    private void HandleDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
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
