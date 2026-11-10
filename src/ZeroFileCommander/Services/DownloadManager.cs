using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;

namespace ZeroFileCommander.Services;

public enum DownloadState
{
    Queued,
    Downloading,
    Paused,
    Completed,
    Failed,
    Canceled,
}

/// <summary>Tek bir indirme. Özellikleri yalnızca UI iş parçacığında değişir.</summary>
public sealed class DownloadItem : INotifyPropertyChanged
{
    private string _fileName = string.Empty;
    private string _filePath = string.Empty;
    private long _received;
    private long? _total;
    private DownloadState _state;
    private string _speedText = string.Empty;
    private string? _error;

    public DownloadItem(string url, string folder)
    {
        Url = url;
        Folder = folder;
        _fileName = DownloadManager.GuessFileName(url);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Url { get; }
    public string Folder { get; }
    public DateTime Started { get; } = DateTime.Now;
    internal CancellationTokenSource? Cancellation { get; set; }

    public string FileName { get => _fileName; set => Set(ref _fileName, value); }
    public string FilePath { get => _filePath; set => Set(ref _filePath, value); }
    public long ReceivedBytes { get => _received; set { if (Set(ref _received, value)) RaiseDerived(); } }
    public long? TotalBytes { get => _total; set { if (Set(ref _total, value)) RaiseDerived(); } }
    public DownloadState State { get => _state; set { if (Set(ref _state, value)) RaiseDerived(); } }
    public string SpeedText { get => _speedText; set { if (Set(ref _speedText, value)) Raise(nameof(StatusText)); } }
    public string? Error { get => _error; set { if (Set(ref _error, value)) Raise(nameof(StatusText)); } }

    public bool IsActive => State is DownloadState.Downloading or DownloadState.Queued;
    public bool IsIndeterminate => State == DownloadState.Downloading && TotalBytes is null or 0;
    public double Progress => TotalBytes is > 0 ? Math.Min(100, ReceivedBytes * 100.0 / TotalBytes.Value) : (State == DownloadState.Completed ? 100 : 0);

    public Microsoft.UI.Xaml.Visibility PauseVisibility => Vis(State == DownloadState.Downloading);
    public Microsoft.UI.Xaml.Visibility ResumeVisibility => Vis(State is DownloadState.Paused or DownloadState.Failed);
    public Microsoft.UI.Xaml.Visibility CancelVisibility => Vis(State is DownloadState.Downloading or DownloadState.Paused or DownloadState.Queued);
    public Microsoft.UI.Xaml.Visibility OpenVisibility => Vis(State == DownloadState.Completed);
    public Microsoft.UI.Xaml.Visibility ProgressVisibility => Vis(State != DownloadState.Completed);

    public string StatusText => State switch
    {
        DownloadState.Queued => "Sırada",
        DownloadState.Downloading => TotalBytes is > 0
            ? $"{DownloadManager.FormatSize(ReceivedBytes)} / {DownloadManager.FormatSize(TotalBytes.Value)} · {SpeedText}"
            : $"{DownloadManager.FormatSize(ReceivedBytes)} · {SpeedText}",
        DownloadState.Paused => $"Duraklatıldı · {DownloadManager.FormatSize(ReceivedBytes)}",
        DownloadState.Completed => $"Tamamlandı · {DownloadManager.FormatSize(ReceivedBytes)}",
        DownloadState.Failed => $"Başarısız: {Error}",
        DownloadState.Canceled => "İptal edildi",
        _ => string.Empty,
    };

    private static Microsoft.UI.Xaml.Visibility Vis(bool visible) =>
        visible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    private void RaiseDerived()
    {
        Raise(nameof(Progress));
        Raise(nameof(IsIndeterminate));
        Raise(nameof(IsActive));
        Raise(nameof(StatusText));
        Raise(nameof(PauseVisibility));
        Raise(nameof(ResumeVisibility));
        Raise(nameof(CancelVisibility));
        Raise(nameof(OpenVisibility));
        Raise(nameof(ProgressVisibility));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Basit, sürdürülebilir (Range destekli) HTTP indirme yöneticisi.</summary>
public sealed class DownloadManager
{
    public static DownloadManager Current { get; } = new();

    private static readonly HttpClient Http = CreateClient();
    private DispatcherQueue? _dispatcher;

    public ObservableCollection<DownloadItem> Items { get; } = new();

    /// <summary>Bir indirme tamamlandığında (UI iş parçacığında).</summary>
    public event EventHandler<DownloadItem>? Completed;

    /// <summary>Toplam ilerleme değiştiğinde (UI iş parçacığında, en fazla ~4/sn).</summary>
    public event EventHandler? ProgressChanged;

    private DownloadManager() { }

    public void Attach(DispatcherQueue dispatcher) => _dispatcher = dispatcher;

    public int ActiveCount => Items.Count(i => i.State == DownloadState.Downloading);

    /// <summary>Etkin indirmelerin toplam ilerlemesi: null = yok, NaN = boyut bilinmiyor.</summary>
    public double? TotalProgress
    {
        get
        {
            var active = Items.Where(i => i.State == DownloadState.Downloading).ToList();
            if (active.Count == 0) return null;
            if (active.Any(i => i.TotalBytes is null or 0)) return double.NaN;
            long total = active.Sum(i => i.TotalBytes!.Value);
            long received = active.Sum(i => i.ReceivedBytes);
            return total == 0 ? double.NaN : (double)received / total;
        }
    }

    public DownloadItem Start(string url, string folder)
    {
        var item = new DownloadItem(url, folder);
        Items.Insert(0, item);
        _ = RunAsync(item);
        return item;
    }

    public void Pause(DownloadItem item)
    {
        if (item.State != DownloadState.Downloading) return;
        item.State = DownloadState.Paused;
        item.Cancellation?.Cancel();
    }

    public void Resume(DownloadItem item)
    {
        if (item.State is DownloadState.Paused or DownloadState.Failed) _ = RunAsync(item);
    }

    public void Cancel(DownloadItem item)
    {
        var wasRunning = item.State == DownloadState.Downloading;
        item.State = DownloadState.Canceled;
        item.Cancellation?.Cancel();
        if (!wasRunning) TryDelete(PartPath(item));
        RaiseProgress();
    }

    public void Remove(DownloadItem item)
    {
        if (item.IsActive || item.State == DownloadState.Paused) Cancel(item);
        Items.Remove(item);
        RaiseProgress();
    }

    public void ClearFinished()
    {
        foreach (var item in Items.Where(i => i.State is DownloadState.Completed or DownloadState.Canceled or DownloadState.Failed).ToList())
            Items.Remove(item);
    }

    private async Task RunAsync(DownloadItem item)
    {
        var cts = new CancellationTokenSource();
        item.Cancellation = cts;
        item.Error = null;
        item.State = DownloadState.Downloading;
        RaiseProgress();

        try
        {
            Directory.CreateDirectory(item.Folder);
            string finalPath = item.FilePath;
            string part = PartPath(item);
            long existing = item.FilePath.Length > 0 && File.Exists(part) ? new FileInfo(part).Length : 0;

            using var request = new HttpRequestMessage(HttpMethod.Get, item.Url);
            if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            bool resumed = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (!resumed) existing = 0;

            if (finalPath.Length == 0)
            {
                var name = GuessFileName(item.Url, response.Content.Headers.ContentDisposition);
                var path = UniquePath(Path.Combine(item.Folder, name));
                finalPath = path;
                OnUi(() =>
                {
                    item.FileName = Path.GetFileName(path);
                    item.FilePath = path;
                });
                part = path + ".zfcpart";
            }

            long? length = response.Content.Headers.ContentLength;
            OnUi(() =>
            {
                item.TotalBytes = length is null ? null : length + existing;
                item.ReceivedBytes = existing;
            });

            await using (var source = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
            await using (var target = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                long received = existing;
                long lastBytes = received;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                long lastTick = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cts.Token).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cts.Token).ConfigureAwait(false);
                    received += read;

                    long now = clock.ElapsedMilliseconds;
                    if (now - lastTick >= 250)
                    {
                        double seconds = (now - lastTick) / 1000.0;
                        double speed = (received - lastBytes) / Math.Max(seconds, 0.001);
                        lastTick = now;
                        lastBytes = received;
                        long snapshot = received;
                        OnUi(() =>
                        {
                            item.ReceivedBytes = snapshot;
                            item.SpeedText = $"{FormatSize((long)speed)}/sn";
                            ProgressChanged?.Invoke(this, EventArgs.Empty);
                        });
                    }
                }
                long final = received;
                OnUi(() => item.ReceivedBytes = final);
            }

            File.Move(part, finalPath, overwrite: false);
            OnUi(() =>
            {
                item.State = DownloadState.Completed;
                item.TotalBytes ??= item.ReceivedBytes;
                ProgressChanged?.Invoke(this, EventArgs.Empty);
                Completed?.Invoke(this, item);
            });
        }
        catch (OperationCanceledException)
        {
            OnUi(() =>
            {
                if (item.State == DownloadState.Canceled) TryDelete(PartPath(item));
                RaiseProgress();
            });
        }
        catch (Exception ex)
        {
            OnUi(() =>
            {
                item.Error = ex.Message;
                item.State = DownloadState.Failed;
                RaiseProgress();
            });
        }
        finally
        {
            cts.Dispose();
            if (ReferenceEquals(item.Cancellation, cts)) item.Cancellation = null;
        }
    }

    private static string PartPath(DownloadItem item) =>
        item.FilePath.Length > 0 ? item.FilePath + ".zfcpart" : string.Empty;

    private void RaiseProgress() => ProgressChanged?.Invoke(this, EventArgs.Empty);

    private void OnUi(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess) action();
        else _dispatcher.TryEnqueue(() => action());
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (path.Length > 0 && File.Exists(path)) File.Delete(path); // yalnızca yarım .zfcpart dosyası
        }
        catch
        {
            // kilitliyse kalsın
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ZeroFileCommander/0.2 (+https://github.com/aliapaydin/zero-file-commander)");
        return client;
    }

    public static bool LooksLikeDownloadUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('\n')) return false;
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        var ext = Path.GetExtension(uri.AbsolutePath).TrimStart('.').ToLowerInvariant();
        return ext is "zip" or "7z" or "rar" or "exe" or "msi" or "msix" or "iso" or "pdf" or "mp4" or "mkv" or "mp3"
            or "flac" or "jpg" or "png" or "gz" or "tar" or "dmg" or "apk" or "csv" or "xlsx" or "docx" or "pptx";
    }

    public static string GuessFileName(string url, ContentDispositionHeaderValue? disposition = null)
    {
        var fromHeader = disposition?.FileNameStar ?? disposition?.FileName;
        if (!string.IsNullOrWhiteSpace(fromHeader)) return Sanitize(fromHeader.Trim('"'));
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var last = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
            if (!string.IsNullOrWhiteSpace(last)) return Sanitize(last);
            return Sanitize(uri.Host) + ".download";
        }
        return "indirme";
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length > 180 ? name[..180] : name;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path) && !File.Exists(path + ".zfcpart")) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !File.Exists(candidate + ".zfcpart")) return candidate;
        }
    }

    public static string FormatSize(long bytes) => Models.FileItem.FormatSize(bytes);
}
