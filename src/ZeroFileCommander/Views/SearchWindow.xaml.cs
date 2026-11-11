using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using ZeroFileCommander.Models;
using ZeroFileCommander.Services;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace ZeroFileCommander.Views;

/// <summary>Alt klasörlerde ada/içeriğe göre arama; sonuçlar bulundukça listelenir.</summary>
public sealed partial class SearchWindow : Window
{
    private const long MaxContentFileSize = 64L * 1024 * 1024;

    private readonly ConcurrentQueue<FileItem> _pending = new();
    private readonly DispatcherQueueTimer _drainTimer;
    private CancellationTokenSource? _cts;
    private int _scanned;
    private string _currentFolder = string.Empty;

    public ObservableCollection<FileItem> Results { get; } = new();

    /// <summary>"Panelde göster": dosyanın tam yolu.</summary>
    public event EventHandler<string>? RevealRequested;

    public SearchWindow() : this(string.Empty) { }

    public SearchWindow(string folder)
    {
        InitializeComponent();
        Loc.Apply(this); // XAML metinleri seçili dile
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 720));
        RootGrid.RequestedTheme = ThemeHelper.ToElementTheme(AppSettings.Current.Theme);
        FolderBox.Text = folder;

        _drainTimer = DispatcherQueue.CreateTimer();
        _drainTimer.Interval = TimeSpan.FromMilliseconds(120);
        _drainTimer.Tick += (_, _) => Drain();
        Closed += (_, _) => _cts?.Cancel();
        Activated += (_, _) => PatternBox.Focus(FocusState.Programmatic);
    }

    public void SetFolder(string folder)
    {
        if (_cts is null) FolderBox.Text = folder;
    }

    private void Box_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        _ = StartAsync();
        e.Handled = true;
    }

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is not null) _cts.Cancel();
        else _ = StartAsync();
    }

    private async Task StartAsync()
    {
        if (_cts is not null) return;
        var root = FolderBox.Text.Trim();
        if (!Directory.Exists(root))
        {
            StatusText.Text = Loc.T("Klasör bulunamadı.");
            return;
        }

        var patterns = PatternBox.Text.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (patterns.Length == 0) patterns = ["*"];
        var nameRegexes = patterns
            .Select(p => new Regex("^" + Regex.Escape(p.Contains('*') || p.Contains('?') ? p : $"*{p}*").Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToList();
        var content = ContentBox.Text;
        var comparison = CaseCheck.IsChecked == true ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase;
        bool recursive = RecursiveCheck.IsChecked == true;
        bool folders = FoldersCheck.IsChecked == true && content.Length == 0;

        Results.Clear();
        while (_pending.TryDequeue(out _)) { }
        _scanned = 0;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SearchIcon.Glyph = "";
        SearchLabel.Text = Loc.T("Durdur");
        _drainTimer.Start();
        var started = DateTime.Now;

        try
        {
            await Task.Run(() => Scan(root, nameRegexes, content, comparison, recursive, folders, token), token);
            Drain();
            StatusText.Text = Loc.F("{0} sonuç · {1:N0} öğe tarandı · {2:0.0} sn", Results.Count, _scanned, (DateTime.Now - started).TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            Drain();
            StatusText.Text = Loc.F("Durduruldu · {0} sonuç", Results.Count);
        }
        finally
        {
            _drainTimer.Stop();
            _cts.Dispose();
            _cts = null;
            SearchIcon.Glyph = "";
            SearchLabel.Text = Loc.T("Ara");
        }
    }

    private void Scan(string root, List<Regex> names, string content, StringComparison comparison, bool recursive, bool folders, CancellationToken token)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = recursive,
            AttributesToSkip = System.IO.FileAttributes.System,
            ReturnSpecialDirectories = false,
        };
        foreach (var info in new DirectoryInfo(root).EnumerateFileSystemInfos("*", options))
        {
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _scanned);
            bool isDirectory = info is DirectoryInfo;
            if (isDirectory && !folders) { _currentFolder = info.FullName; continue; }
            if (!names.Any(r => r.IsMatch(info.Name))) continue;

            if (content.Length > 0)
            {
                if (isDirectory || info is not FileInfo file || file.Length > MaxContentFileSize) continue;
                if (!ContainsText(file.FullName, content, comparison, token)) continue;
            }

            _pending.Enqueue(new FileItem
            {
                Name = info.Name,
                FullPath = info.FullName,
                IsDirectory = isDirectory,
                Size = (info as FileInfo)?.Length,
                Modified = info.LastWriteTime,
                // Sonuç listesinde "Klasör" sütunu için kök klasöre göreli yol
                EntryName = Path.GetRelativePath(root, Path.GetDirectoryName(info.FullName) ?? root),
            });
        }
    }

    private static bool ContainsText(string path, string text, StringComparison comparison, CancellationToken token)
    {
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16);
            // Satır sınırında bölünen eşleşmeleri kaçırmamak için önceki parçanın sonunu taşı.
            var buffer = new char[1 << 16];
            string carry = string.Empty;
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                var chunk = carry + new string(buffer, 0, read);
                if (chunk.Contains(text, comparison)) return true;
                carry = chunk.Length > text.Length ? chunk[^text.Length..] : chunk;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        return false;
    }

    private void Drain()
    {
        int added = 0;
        while (added < 500 && _pending.TryDequeue(out var item))
        {
            Results.Add(item);
            added++;
        }
        if (_cts is not null)
        {
            var folder = _currentFolder;
            StatusText.Text = Loc.F("{0} sonuç · {1:N0} öğe tarandı · {2}", Results.Count, _scanned, folder);
        }
    }

    private FileItem? Selected => ResultList.SelectedItem as FileItem;

    private void Reveal(FileItem? item)
    {
        if (item is null) return;
        RevealRequested?.Invoke(this, item.FullPath);
    }

    private void ResultList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => Reveal(Selected);

    private void ResultList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            Reveal(Selected);
            e.Handled = true;
        }
    }

    private void Reveal_Click(object sender, RoutedEventArgs e) => Reveal(Selected);

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } item) ShellActions.Open(item.FullPath);
    }
}
