using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace ZeroFileCommander.Services;

public sealed record NotificationAction(string Label, Action Action, bool IsPrimary = false);

/// <summary>
/// Uygulamanın kendi bildirim kutusu: ekranın seçilen köşesinde beliren, odağı çalmayan,
/// birkaç saniye sonra kaybolan küçük bir pencere. Birden fazlası üst üste dizilir.
/// </summary>
public static class Notifier
{
    private const int LogicalWidth = 380;
    private const int LogicalHeight = 132;
    private const int Margin = 16;
    private static readonly List<Window> Open = new();

    public static void Show(string title, string message, string glyph = "", params NotificationAction[] actions)
    {
        var window = new Window { Title = title };
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        double scale = GetDpiForWindow(hwnd) / 96.0;

        var appWindow = window.AppWindow;
        appWindow.IsShownInSwitchers = false;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }
        window.SystemBackdrop = new DesktopAcrylicBackdrop();

        var root = BuildContent(window, title, message, glyph, actions);
        root.RequestedTheme = ThemeHelper.ToElementTheme(AppSettings.Current.Theme);
        window.Content = root;

        int width = (int)(LogicalWidth * scale);
        int height = (int)(LogicalHeight * scale);
        Open.Add(window);
        appWindow.MoveAndResize(ComputeRect(width, height, Open.Count - 1, scale));
        appWindow.Show(false);

        // Otomatik kapanma; fare üzerindeyken bekler.
        bool hovering = false;
        root.PointerEntered += (_, _) => hovering = true;
        root.PointerExited += (_, _) => hovering = false;
        var timer = window.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        int remaining = 7;
        timer.Tick += (t, _) =>
        {
            if (hovering) return;
            if (--remaining > 0) return;
            t.Stop();
            Close(window);
        };
        timer.Start();
    }

    private static FrameworkElement BuildContent(Window window, string title, string message, string glyph, NotificationAction[] actions)
    {
        var grid = new Grid
        {
            Padding = new Thickness(16, 14, 12, 14),
            ColumnSpacing = 14,
            RowSpacing = 10,
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var badge = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Top,
            Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            Child = new FontIcon
            {
                Glyph = glyph,
                FontSize = 18,
                Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"],
            },
        };
        grid.Children.Add(badge);

        var texts = new StackPanel { Spacing = 2 };
        texts.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        texts.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var close = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 11 },
            Padding = new Thickness(6),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        AutomationProperties_SetName(close, "Kapat");
        close.Click += (_, _) => Close(window);
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        if (actions.Length > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var action in actions)
            {
                var button = new Button { Content = action.Label };
                if (action.IsPrimary) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
                button.Click += (_, _) =>
                {
                    action.Action();
                    Close(window);
                };
                row.Children.Add(button);
            }
            Grid.SetRow(row, 1);
            Grid.SetColumnSpan(row, 3);
            grid.Children.Add(row);
        }

        return grid;
    }

    private static void AutomationProperties_SetName(DependencyObject element, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);

    private static RectInt32 ComputeRect(int width, int height, int index, double scale)
    {
        var area = DisplayArea.Primary.WorkArea;
        int margin = (int)(Margin * scale);
        int step = height + (int)(8 * scale);
        var position = AppSettings.Current.NotificationPosition;

        int x = position switch
        {
            NotificationPosition.BottomLeft or NotificationPosition.TopLeft => area.X + margin,
            NotificationPosition.BottomCenter => area.X + (area.Width - width) / 2,
            _ => area.X + area.Width - width - margin,
        };
        bool top = position is NotificationPosition.TopLeft or NotificationPosition.TopRight;
        int y = top
            ? area.Y + margin + index * step
            : area.Y + area.Height - height - margin - index * step;

        return new RectInt32(x, y, width, height);
    }

    private static void Close(Window window)
    {
        if (!Open.Remove(window)) return;
        window.Close();

        // Kalanları yeniden diz.
        for (int i = 0; i < Open.Count; i++)
        {
            var w = Open[i];
            var size = w.AppWindow.Size;
            double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(w)) / 96.0;
            w.AppWindow.Move(new PointInt32(ComputeRect(size.Width, size.Height, i, scale).X, ComputeRect(size.Width, size.Height, i, scale).Y));
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}

public static class ThemeHelper
{
    public static ElementTheme ToElementTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };
}
