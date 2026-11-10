using System.Runtime.CompilerServices;
using Windows.Storage;

namespace ZeroFileCommander.Services;

public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2,
}

public enum NotificationPosition
{
    BottomRight = 0,
    TopRight = 1,
    BottomLeft = 2,
    TopLeft = 3,
    BottomCenter = 4,
}

/// <summary>Kalıcı kullanıcı ayarları (paketli uygulamanın LocalSettings deposu).</summary>
public sealed class AppSettings
{
    public static AppSettings Current { get; } = new();

    private readonly ApplicationDataContainer _store = ApplicationData.Current.LocalSettings;

    /// <summary>Değişen ayarın adını taşır.</summary>
    public event EventHandler<string>? Changed;

    private AppSettings() { }

    // ---- Görünüm ----
    public AppTheme Theme { get => (AppTheme)Get(0); set => Set((int)value); }
    public bool ShowPreviewPane { get => Get(true); set => Set(value); }
    public bool ShowFolderTree { get => Get(false); set => Set(value); }
    public bool ShowRibbon { get => Get(true); set => Set(value); }
    public bool HoverPreview { get => Get(true); set => Set(value); }
    public int HoverDelayMs { get => Get(350); set => Set(value); }

    // ---- Arka plan ----
    /// <summary>Pencere kapatılınca uygulama sistem tepsisinde çalışmaya devam eder.</summary>
    public bool RunInBackground { get => Get(true); set => Set(value); }
    /// <summary>Simge durumuna küçültünce görev çubuğundan da gizlenip tepsiye iner.</summary>
    public bool MinimizeToTray { get => Get(false); set => Set(value); }

    // ---- İndirmeler ----
    public bool DownloadsEnabled { get => Get(false); set => Set(value); }
    public string DownloadFolder
    {
        get => Get(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        set => Set(value);
    }
    public bool CaptureClipboardLinks { get => Get(false); set => Set(value); }

    // ---- Yerleşim (hatırlanır) ----
    public string MainWindowBounds { get => Get(string.Empty); set => Set(value); }
    public string DownloadsWindowBounds { get => Get(string.Empty); set => Set(value); }
    public double TreeWidth { get => Get(250.0); set => Set(value); }
    public double PreviewWidth { get => Get(360.0); set => Set(value); }
    /// <summary>Sol panelin iki panelin toplam genişliğindeki payı (0–1).</summary>
    public double LeftRatio { get => Get(0.5); set => Set(value); }
    public string LeftPath { get => Get(string.Empty); set => Set(value); }
    public string RightPath { get => Get(string.Empty); set => Set(value); }

    // ---- Bildirimler ----
    public NotificationPosition NotificationPosition { get => (NotificationPosition)Get(0); set => Set((int)value); }

    private static readonly string[] LayoutKeys =
    [
        nameof(MainWindowBounds), nameof(DownloadsWindowBounds), nameof(TreeWidth),
        nameof(PreviewWidth), nameof(LeftRatio),
    ];

    /// <summary>Pencere konum/boyutları ve panel genişliklerini varsayılana döndürür.</summary>
    public void ResetLayout()
    {
        foreach (var key in LayoutKeys) _store.Values.Remove(key);
        Changed?.Invoke(this, "Layout");
    }

    /// <summary>Tüm ayarları siler.</summary>
    public void ResetAll()
    {
        _store.Values.Clear();
        Changed?.Invoke(this, "All");
    }

    private T Get<T>(T fallback, [CallerMemberName] string key = "")
    {
        try
        {
            return _store.Values.TryGetValue(key, out var value) && value is T typed ? typed : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private void Set<T>(T value, [CallerMemberName] string key = "")
    {
        _store.Values[key] = value;
        Changed?.Invoke(this, key);
    }
}
