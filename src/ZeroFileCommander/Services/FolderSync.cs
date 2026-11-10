using System.ComponentModel;
using Microsoft.UI.Xaml;

namespace ZeroFileCommander.Services;

public enum SyncState
{
    Same,
    LeftOnly,
    RightOnly,
    LeftNewer,
    RightNewer,
    Different,
}

public enum SyncAction
{
    None,
    CopyRight,
    CopyLeft,
}

/// <summary>Karşılaştırmadaki bir satır (göreli yol + iki tarafın bilgisi + yapılacak işlem).</summary>
public sealed class SyncEntry : INotifyPropertyChanged
{
    private SyncAction _action;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string RelativePath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long? LeftSize { get; set; }
    public long? RightSize { get; set; }
    public DateTime? LeftTime { get; set; }
    public DateTime? RightTime { get; set; }
    public SyncState State { get; set; }

    public SyncAction Action
    {
        get => _action;
        set
        {
            if (_action == value) return;
            _action = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Action)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActionGlyph)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActionText)));
        }
    }

    public string LeftText => Describe(LeftSize, LeftTime, State is SyncState.RightOnly);
    public string RightText => Describe(RightSize, RightTime, State is SyncState.LeftOnly);

    public string StateText => State switch
    {
        SyncState.Same => "Aynı",
        SyncState.LeftOnly => "Yalnızca solda",
        SyncState.RightOnly => "Yalnızca sağda",
        SyncState.LeftNewer => "Solda daha yeni",
        SyncState.RightNewer => "Sağda daha yeni",
        _ => "Farklı",
    };

    public string ActionGlyph => Action switch
    {
        SyncAction.CopyRight => "",
        SyncAction.CopyLeft => "",
        _ => State == SyncState.Same ? "" : "",
    };

    public string ActionText => Action switch
    {
        SyncAction.CopyRight => "Sağa kopyala",
        SyncAction.CopyLeft => "Sola kopyala",
        _ => State == SyncState.Same ? "Aynı" : "Atla",
    };

    public Visibility FolderVisibility => IsDirectory ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Tıklayınca bir sonraki geçerli işleme geç.</summary>
    public void CycleAction()
    {
        Action = State switch
        {
            SyncState.LeftOnly => Action == SyncAction.CopyRight ? SyncAction.None : SyncAction.CopyRight,
            SyncState.RightOnly => Action == SyncAction.CopyLeft ? SyncAction.None : SyncAction.CopyLeft,
            _ => Action switch
            {
                SyncAction.None => SyncAction.CopyRight,
                SyncAction.CopyRight => SyncAction.CopyLeft,
                _ => SyncAction.None,
            },
        };
    }

    private string Describe(long? size, DateTime? time, bool missing)
    {
        if (missing) return "—";
        var sizeText = IsDirectory ? "Klasör" : Models.FileItem.FormatSize(size ?? 0);
        return time is null ? sizeText : $"{sizeText} · {time:dd.MM.yyyy HH:mm}";
    }
}

/// <summary>İki klasörü karşılaştırır ve seçilen yönde kopyalayarak eşitler (asla silmez).</summary>
public static class FolderSync
{
    private static readonly TimeSpan TimeTolerance = TimeSpan.FromSeconds(2); // FAT/exFAT yuvarlaması

    public static Task<List<SyncEntry>> CompareAsync(string left, string right, bool recursive, bool compareContent, CancellationToken token) =>
        Task.Run(() =>
        {
            var result = new List<SyncEntry>();
            Walk(left, right, string.Empty, recursive, compareContent, result, token);
            return result.OrderBy(e => e.RelativePath, StringComparer.CurrentCultureIgnoreCase).ToList();
        }, token);

    private static void Walk(string leftRoot, string rightRoot, string relative, bool recursive, bool compareContent, List<SyncEntry> result, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        var leftDir = new DirectoryInfo(Path.Combine(leftRoot, relative));
        var rightDir = new DirectoryInfo(Path.Combine(rightRoot, relative));

        var leftFiles = leftDir.Exists ? leftDir.EnumerateFiles("*", options).ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase) : new();
        var rightFiles = rightDir.Exists ? rightDir.EnumerateFiles("*", options).ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase) : new();

        foreach (var name in leftFiles.Keys.Union(rightFiles.Keys, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            leftFiles.TryGetValue(name, out var l);
            rightFiles.TryGetValue(name, out var r);
            var entry = new SyncEntry
            {
                RelativePath = Path.Combine(relative, (l ?? r)!.Name),
                LeftSize = l?.Length,
                RightSize = r?.Length,
                LeftTime = l?.LastWriteTime,
                RightTime = r?.LastWriteTime,
            };
            entry.State = l is null ? SyncState.RightOnly
                : r is null ? SyncState.LeftOnly
                : Classify(l, r, compareContent, token);
            entry.Action = DefaultAction(entry.State);
            result.Add(entry);
        }

        var leftDirs = leftDir.Exists ? leftDir.EnumerateDirectories("*", options).ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase) : new();
        var rightDirs = rightDir.Exists ? rightDir.EnumerateDirectories("*", options).ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase) : new();
        foreach (var name in leftDirs.Keys.Union(rightDirs.Keys, StringComparer.OrdinalIgnoreCase))
        {
            leftDirs.TryGetValue(name, out var l);
            rightDirs.TryGetValue(name, out var r);
            var childRelative = Path.Combine(relative, (l ?? r)!.Name);
            if (l is not null && r is not null)
            {
                if (recursive) Walk(leftRoot, rightRoot, childRelative, recursive, compareContent, result, token);
                continue;
            }
            var entry = new SyncEntry
            {
                RelativePath = childRelative,
                IsDirectory = true,
                LeftTime = l?.LastWriteTime,
                RightTime = r?.LastWriteTime,
                State = l is null ? SyncState.RightOnly : SyncState.LeftOnly,
            };
            entry.Action = DefaultAction(entry.State);
            result.Add(entry);
        }
    }

    private static SyncState Classify(FileInfo left, FileInfo right, bool compareContent, CancellationToken token)
    {
        var diff = left.LastWriteTimeUtc - right.LastWriteTimeUtc;
        bool sameTime = diff.Duration() <= TimeTolerance;
        if (left.Length == right.Length && sameTime) return SyncState.Same;
        if (compareContent && left.Length == right.Length && ContentEquals(left.FullName, right.FullName, token)) return SyncState.Same;
        if (sameTime) return SyncState.Different;
        return diff > TimeSpan.Zero ? SyncState.LeftNewer : SyncState.RightNewer;
    }

    private static bool ContentEquals(string a, string b, CancellationToken token)
    {
        const int size = 1 << 16;
        using var fa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, size);
        using var fb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, size);
        var ba = new byte[size];
        var bb = new byte[size];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int ra = fa.ReadAtLeast(ba, size, throwOnEndOfStream: false);
            int rb = fb.ReadAtLeast(bb, size, throwOnEndOfStream: false);
            if (ra != rb) return false;
            if (ra == 0) return true;
            if (!ba.AsSpan(0, ra).SequenceEqual(bb.AsSpan(0, rb))) return false;
        }
    }

    private static SyncAction DefaultAction(SyncState state) => state switch
    {
        SyncState.LeftOnly or SyncState.LeftNewer => SyncAction.CopyRight,
        SyncState.RightOnly or SyncState.RightNewer => SyncAction.CopyLeft,
        _ => SyncAction.None,
    };

    /// <summary>Seçili işlemleri uygular; ilerleme (bitti, toplam) bildirir.</summary>
    public static Task<(int Done, List<string> Errors)> ApplyAsync(string left, string right, IReadOnlyList<SyncEntry> entries,
        IProgress<(int Done, int Total, string Current)> progress, CancellationToken token) => Task.Run(() =>
    {
        var work = entries.Where(e => e.Action != SyncAction.None).ToList();
        var errors = new List<string>();
        int done = 0;
        foreach (var entry in work)
        {
            token.ThrowIfCancellationRequested();
            progress.Report((done, work.Count, entry.RelativePath));
            var (from, to) = entry.Action == SyncAction.CopyRight
                ? (Path.Combine(left, entry.RelativePath), Path.Combine(right, entry.RelativePath))
                : (Path.Combine(right, entry.RelativePath), Path.Combine(left, entry.RelativePath));
            try
            {
                if (entry.IsDirectory) CopyDirectory(from, to, token);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Copy(from, to, overwrite: true);
                }
                done++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"{entry.RelativePath}: {ex.Message}");
            }
        }
        progress.Report((done, work.Count, string.Empty));
        return (done, errors);
    }, token);

    private static void CopyDirectory(string from, string to, CancellationToken token)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
    }
}
