using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace ZeroFileCommander.Services;

/// <summary>
/// Bir pencerenin normal (büyütülmemiş) konum/boyutunu ve büyütülmüş olup olmadığını izler,
/// ayarlara kaydeder ve bir sonraki açılışta geri yükler.
/// </summary>
public sealed class WindowPlacement
{
    private readonly AppWindow _window;
    private readonly Func<string> _load;
    private readonly Action<string> _save;
    private readonly DispatcherQueueTimer _saveTimer;
    private RectInt32 _normal;
    private bool _maximized;
    private bool _restoring;

    public WindowPlacement(AppWindow window, DispatcherQueue dispatcher, Func<string> load, Action<string> save)
    {
        _window = window;
        _load = load;
        _save = save;
        _saveTimer = dispatcher.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(600);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => Save();
        _window.Changed += Window_Changed;
    }

    /// <summary>Kayıtlı yerleşimi uygular; yoksa varsayılan boyutta ortalar.</summary>
    public void Restore(SizeInt32 defaultSize)
    {
        _restoring = true;
        try
        {
            if (TryParse(_load(), out var rect, out var maximized) && IsOnScreen(rect))
            {
                _window.MoveAndResize(rect);
                _normal = rect;
                if (maximized && _window.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Maximize();
                    _maximized = true;
                }
                return;
            }

            var area = DisplayArea.Primary.WorkArea;
            int width = Math.Min(defaultSize.Width, area.Width - 40);
            int height = Math.Min(defaultSize.Height, area.Height - 40);
            var centered = new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
            _window.MoveAndResize(centered);
            _normal = centered;
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <summary>Hemen kaydet (pencere kapanırken).</summary>
    public void Save()
    {
        _saveTimer.Stop();
        if (_normal.Width <= 0 || _normal.Height <= 0) return;
        _save(string.Join(',',
            _normal.X.ToString(CultureInfo.InvariantCulture),
            _normal.Y.ToString(CultureInfo.InvariantCulture),
            _normal.Width.ToString(CultureInfo.InvariantCulture),
            _normal.Height.ToString(CultureInfo.InvariantCulture),
            _maximized ? "1" : "0"));
    }

    private void Window_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_restoring) return;
        if (!args.DidPositionChange && !args.DidSizeChange && !args.DidPresenterChange) return;
        if (!sender.IsVisible) return;

        var state = (sender.Presenter as OverlappedPresenter)?.State ?? OverlappedPresenterState.Restored;
        switch (state)
        {
            case OverlappedPresenterState.Minimized:
                return; // küçültülmüşken son normal yerleşimi koru
            case OverlappedPresenterState.Maximized:
                _maximized = true;
                break;
            default:
                _maximized = false;
                _normal = new RectInt32(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
                break;
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private static bool TryParse(string text, out RectInt32 rect, out bool maximized)
    {
        rect = default;
        maximized = false;
        var parts = text.Split(',');
        if (parts.Length != 5) return false;
        var numbers = new int[4];
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i])) return false;
        }
        if (numbers[2] < 200 || numbers[3] < 150) return false;
        rect = new RectInt32(numbers[0], numbers[1], numbers[2], numbers[3]);
        maximized = parts[4] == "1";
        return true;
    }

    /// <summary>Başlık çubuğunun bir kısmı bağlı ekranlardan birinde görünüyor mu (monitör çıkarılmış olabilir).</summary>
    private static bool IsOnScreen(RectInt32 rect)
    {
        var titleStrip = new RectInt32(rect.X + 40, rect.Y, Math.Max(1, rect.Width - 80), 40);
        var area = DisplayArea.GetFromRect(titleStrip, DisplayAreaFallback.None);
        return area is not null;
    }
}
