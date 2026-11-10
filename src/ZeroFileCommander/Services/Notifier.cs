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
        try
        {
            ShowCore(title, message, glyph, actions);
        }
        catch (Exception ex)
        {
            // Bildirim gösterilemese bile uygulama çalışmaya devam etmeli.
            CrashLog.Write("Notifier", ex);
        }
    }

    private static void ShowCore(string title, string message, string glyph, NotificationAction[] actions)
    {
        var window = new Window { Title = title };
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        double scale = Math.Max(1.0, GetDpiForWindow(hwnd) / 96.0);

        var appWindow = window.AppWindow;
        appWindow.IsShownInSwitchers = false;

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        try
        {
            presenter.SetBorderAndTitleBar(true, false);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Notifier.SetBorderAndTitleBar", ex);
        }
        appWindow.SetPresenter(presenter);

        try
        {
            window.SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Notifier.Backdrop", ex);
        }

        var root = BuildContent(window, title, message, glyph, actions);
        root.RequestedTheme = ThemeHelper.ToElementTheme(AppSettings.Current.Theme);
        window.Content = root;

        int width = (int)(LogicalWidth * scale);
        int height = (int)(LogicalHeight * scale);
        Open.Add(window);
        appWindow.MoveAndResize(ComputeRect(width, height, Open.Count - 1, scale));

        // WinUI penceresi içeriğini ancak etkinleştirilince çiziyor. Etkinleştir,
        // sonra odağı hemen önceki pencereye geri ver (kullanıcının yazdığı yer kaybolmasın).
        var previous = GetForegroundWindow();
        window.Activate();
        if (previous != IntPtr.Zero && previous != hwnd) SetForegroundWindow(previous);

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
        var resources = Application.Current.Resources;

        var grid = new Grid
        {
            Padding = new Thickness(16, 14, 12, 14),
            ColumnSpacing = 14,
            RowSpacing = 10,
            Background = new SolidColorBrush(Colors.Transparent),
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
            Background = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x1F, 0x6F, 0xD6)),
            Child = new FontIcon { Glyph = glyph, FontSize = 18, Foreground = new SolidColorBrush(Colors.White) },
        };
        grid.Children.Add(badge);

        var texts = new StackPanel { Spacing = 2 };
        texts.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        texts.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = 0.8,
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
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(close, "Kapat");
        close.Click += (_, _) => Close(window);
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        if (actions.Length > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var action in actions)
            {
                var button = new Button { Content = action.Label };
                if (action.IsPrimary && resources.TryGetValue("AccentButtonStyle", out var style) && style is Style s)
                    button.Style = s;
                button.Click += (_, _) =>
                {
                    try
                    {
                        action.Action();
                    }
                    catch (Exception ex)
                    {
                        CrashLog.Write("Notifier.Action", ex);
                    }
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
        try
        {
            window.Close();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Notifier.Close", ex);
        }

        // Kalanları yeniden diz.
        for (int i = 0; i < Open.Count; i++)
        {
            var w = Open[i];
            var size = w.AppWindow.Size;
            double scale = Math.Max(1.0, GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(w)) / 96.0);
            var rect = ComputeRect(size.Width, size.Height, i, scale);
            w.AppWindow.Move(new PointInt32(rect.X, rect.Y));
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
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
