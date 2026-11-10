using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ZeroFileCommander.Services;

public enum TransferKind
{
    Copy,
    Move,
}

public enum TransferState
{
    Queued,
    Scanning,
    Running,
    Paused,
    WaitingForConflicts,
    Completed,
    CompletedWithErrors,
    Canceled,
}

/// <summary>Hedefte aynı adlı dosya varsa ne yapılacağı.</summary>
public enum ConflictPolicy
{
    /// <summary>Çakışanları sona bırak, diğerlerine devam et; kullanıcı sonra karar verir.</summary>
    AskLater = 0,
    OverwriteIfNewer = 1,
    Skip = 2,
    Overwrite = 3,
    KeepBoth = 4,
}

/// <summary>Kuyruktaki bir kopyalama/taşıma işi. Özellikleri yalnızca UI iş parçacığında değişir.</summary>
public sealed class TransferJob : INotifyPropertyChanged
{
    private TransferState _state = TransferState.Queued;
    private long _bytesTotal;
    private long _bytesDone;
    private int _filesTotal;
    private int _filesDone;
    private string _current = string.Empty;
    private string _speedText = string.Empty;
    private string _etaText = string.Empty;
    private int _conflictCount;
    private int _errorCount;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TransferJob(TransferKind kind, IReadOnlyList<string> sources, string target, ConflictPolicy policy)
    {
        Kind = kind;
        Sources = sources;
        Target = target;
        Policy = policy;
    }

    public TransferKind Kind { get; }
    public IReadOnlyList<string> Sources { get; }
    public string Target { get; }
    public ConflictPolicy Policy { get; set; }
    public DateTime Created { get; } = DateTime.Now;

    /// <summary>Kaynaklar bu uzak bağlantıdaysa (indirme). Kaynaklar o zaman uzak adreslerdir.</summary>
    public Connections.SavedConnection? SourceConnection { get; init; }

    /// <summary>Hedef bu uzak bağlantıdaysa (yükleme). Hedef o zaman uzak adrestir.</summary>
    public Connections.SavedConnection? TargetConnection { get; init; }

    public bool IsRemote => SourceConnection is not null || TargetConnection is not null;

    internal List<(string Source, string Destination, long Size)> PendingConflicts { get; } = new();
    internal List<string> Errors { get; } = new();
    internal ManualResetEventSlim Gate { get; } = new(true);
    internal CancellationTokenSource Cancellation { get; } = new();
    internal TaskCompletionSource<ConflictPolicy>? ConflictDecision { get; set; }

    public string Title
    {
        get
        {
            string what = Sources.Count == 1 ? Path.GetFileName(Sources[0].TrimEnd('\\')) : $"{Sources.Count} öğe";
            string verb = SourceConnection is not null ? (Kind == TransferKind.Copy ? "İndiriliyor" : "İndirilip taşınıyor")
                : TargetConnection is not null ? (Kind == TransferKind.Copy ? "Yükleniyor" : "Yüklenip taşınıyor")
                : Kind == TransferKind.Copy ? "Kopyalanıyor" : "Taşınıyor";
            return $"{verb}: {what}";
        }
    }

    public string RouteText => $"→ {Target}";

    public TransferState State { get => _state; set { if (Set(ref _state, value)) RaiseDerived(); } }
    public long BytesTotal { get => _bytesTotal; set { if (Set(ref _bytesTotal, value)) RaiseDerived(); } }
    public long BytesDone { get => _bytesDone; set { if (Set(ref _bytesDone, value)) RaiseDerived(); } }
    public int FilesTotal { get => _filesTotal; set { if (Set(ref _filesTotal, value)) RaiseDerived(); } }
    public int FilesDone { get => _filesDone; set { if (Set(ref _filesDone, value)) RaiseDerived(); } }
    public string Current { get => _current; set => Set(ref _current, value); }
    public string SpeedText { get => _speedText; set { if (Set(ref _speedText, value)) Raise(nameof(StatusText)); } }
    public string EtaText { get => _etaText; set { if (Set(ref _etaText, value)) Raise(nameof(StatusText)); } }
    public int ConflictCount { get => _conflictCount; set { if (Set(ref _conflictCount, value)) RaiseDerived(); } }
    public int ErrorCount { get => _errorCount; set { if (Set(ref _errorCount, value)) RaiseDerived(); } }

    public bool IsFinished => State is TransferState.Completed or TransferState.CompletedWithErrors or TransferState.Canceled;
    public bool IsActive => State is TransferState.Scanning or TransferState.Running;

    public double Progress => BytesTotal > 0 ? Math.Min(100, BytesDone * 100.0 / BytesTotal) : (State == TransferState.Completed ? 100 : 0);
    public bool IsIndeterminate => State is TransferState.Scanning or TransferState.Queued;

    public string StatusText => State switch
    {
        TransferState.Queued => "Sırada",
        TransferState.Scanning => "Hazırlanıyor…",
        TransferState.Running => $"{FilesDone}/{FilesTotal} dosya · {Models.FileItem.FormatSize(BytesDone)} / {Models.FileItem.FormatSize(BytesTotal)} · {SpeedText} · {EtaText}",
        TransferState.Paused => $"Duraklatıldı · {FilesDone}/{FilesTotal} dosya",
        TransferState.WaitingForConflicts => $"{ConflictCount} dosya hedefte zaten var — ne yapılsın?",
        TransferState.Completed => $"Tamamlandı · {FilesDone} dosya · {Models.FileItem.FormatSize(BytesDone)}",
        TransferState.CompletedWithErrors => $"{FilesDone} dosya tamam, {ErrorCount} hata",
        TransferState.Canceled => $"İptal edildi · {FilesDone} dosya tamamlanmıştı",
        _ => string.Empty,
    };

    public string ErrorSummary => string.Join("\n", Errors.Take(5));

    public Visibility PauseVisibility => Vis(State is TransferState.Running or TransferState.Scanning);
    public Visibility ResumeVisibility => Vis(State == TransferState.Paused);
    public Visibility CancelVisibility => Vis(!IsFinished);
    public Visibility ConflictVisibility => Vis(State == TransferState.WaitingForConflicts);
    public Visibility ErrorVisibility => Vis(ErrorCount > 0);
    public Visibility RemoveVisibility => Vis(IsFinished);

    private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void RaiseDerived()
    {
        foreach (var name in new[]
                 {
                     nameof(Progress), nameof(IsIndeterminate), nameof(StatusText), nameof(IsFinished), nameof(IsActive),
                     nameof(PauseVisibility), nameof(ResumeVisibility), nameof(CancelVisibility), nameof(ConflictVisibility),
                     nameof(ErrorVisibility), nameof(RemoveVisibility), nameof(ErrorSummary),
                 })
            Raise(name);
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

/// <summary>
/// Zero'nun kopyalama motoru: işler sırayla çalışır, duraklatılabilir, hız/kalan süre gösterir.
/// Çakışmalar kuyruğu durdurmaz; "sonra sor" kuralında çakışanlar sona bırakılır.
/// Yarım dosya bırakmamak için önce geçici ada kopyalar, bitince yerine koyar.
/// </summary>
public sealed partial class TransferManager
{
    public static TransferManager Current { get; } = new();

    private const int BufferSize = 1 << 20;
    private DispatcherQueue? _dispatcher;
    private bool _running;

    public ObservableCollection<TransferJob> Jobs { get; } = new();

    /// <summary>İlerleme değiştiğinde (UI iş parçacığında, ~4/sn).</summary>
    public event EventHandler? ProgressChanged;

    /// <summary>Bir iş bittiğinde (UI iş parçacığında).</summary>
    public event EventHandler<TransferJob>? JobFinished;

    /// <summary>Bir iş çakışma kararı beklemeye başladığında.</summary>
    public event EventHandler<TransferJob>? ConflictsWaiting;

    private TransferManager() { }

    public void Attach(DispatcherQueue dispatcher) => _dispatcher = dispatcher;

    public int ActiveCount => Jobs.Count(j => !j.IsFinished);

    /// <summary>Etkin işlerin toplam ilerlemesi (0–1); iş yoksa null.</summary>
    public double? TotalProgress
    {
        get
        {
            var active = Jobs.Where(j => !j.IsFinished).ToList();
            if (active.Count == 0) return null;
            long total = active.Sum(j => j.BytesTotal);
            if (total == 0) return double.NaN;
            return (double)active.Sum(j => j.BytesDone) / total;
        }
    }

    public TransferJob Enqueue(TransferKind kind, IReadOnlyList<string> sources, string target, ConflictPolicy policy) =>
        Enqueue(new TransferJob(kind, sources, target, policy));

    /// <summary>Uzak bağlantıya yükleme ya da oradan indirme işi ekler.</summary>
    public TransferJob EnqueueRemote(TransferKind kind, IReadOnlyList<string> sources, string target, ConflictPolicy policy,
        Connections.SavedConnection? source, Connections.SavedConnection? destination) =>
        Enqueue(new TransferJob(kind, sources, target, policy) { SourceConnection = source, TargetConnection = destination });

    private TransferJob Enqueue(TransferJob job)
    {
        Jobs.Insert(0, job);
        RaiseProgress();
        if (!_running) _ = RunQueueAsync();
        return job;
    }

    public void Pause(TransferJob job)
    {
        if (!job.IsActive) return;
        job.Gate.Reset();
        job.State = TransferState.Paused;
        RaiseProgress();
    }

    public void Resume(TransferJob job)
    {
        if (job.State != TransferState.Paused) return;
        job.State = TransferState.Running;
        job.Gate.Set();
        RaiseProgress();
    }

    public void Cancel(TransferJob job)
    {
        if (job.IsFinished) return;
        job.Cancellation.Cancel();
        job.Gate.Set();
        job.ConflictDecision?.TrySetResult(ConflictPolicy.Skip);
        if (job.State == TransferState.Queued) job.State = TransferState.Canceled;
        RaiseProgress();
    }

    /// <summary>Bekleyen çakışmalar için kararı ver; iş kaldığı yerden sürer.</summary>
    public void ResolveConflicts(TransferJob job, ConflictPolicy decision) =>
        job.ConflictDecision?.TrySetResult(decision);

    public void Remove(TransferJob job)
    {
        if (!job.IsFinished) return;
        Jobs.Remove(job);
        RaiseProgress();
    }

    public void ClearFinished()
    {
        foreach (var job in Jobs.Where(j => j.IsFinished).ToList()) Jobs.Remove(job);
        RaiseProgress();
    }

    private async Task RunQueueAsync()
    {
        _running = true;
        try
        {
            while (true)
            {
                var next = Jobs.LastOrDefault(j => j.State == TransferState.Queued);
                if (next is null) break;
                await RunJobAsync(next);
                JobFinished?.Invoke(this, next);
                RaiseProgress();
            }
        }
        finally
        {
            _running = false;
        }
    }

    private async Task RunJobAsync(TransferJob job)
    {
        if (job.IsRemote)
        {
            await RunRemoteJobAsync(job);
            return;
        }
        var token = job.Cancellation.Token;
        job.State = TransferState.Scanning;
        RaiseProgress();

        try
        {
            // 1) Plan: hangi dosya nereye (klasörler içerikleriyle)
            var plan = await Task.Run(() => BuildPlan(job, token), token);
            long total = plan.Sum(p => p.Size);
            job.FilesTotal = plan.Count(p => !p.IsDirectory);
            job.BytesTotal = total;
            job.State = TransferState.Running;

            // 2) Aynı sürücüde taşıma: yeniden adlandırma kadar hızlı
            if (job.Kind == TransferKind.Move && await Task.Run(() => TryFastMove(job, token), token))
            {
                job.FilesDone = job.FilesTotal;
                job.BytesDone = job.BytesTotal;
                job.State = TransferState.Completed;
                return;
            }

            // 3) Kopyala
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            long done = 0;
            long lastReport = 0;
            var speedSamples = new Queue<(long Ms, long Bytes)>();

            void Report(string current, bool force = false)
            {
                long now = stopwatch.ElapsedMilliseconds;
                if (!force && now - lastReport < 250) return;
                lastReport = now;
                speedSamples.Enqueue((now, done));
                while (speedSamples.Count > 1 && now - speedSamples.Peek().Ms > 3000) speedSamples.Dequeue();
                var first = speedSamples.Peek();
                double seconds = Math.Max(0.25, (now - first.Ms) / 1000.0);
                double speed = (done - first.Bytes) / seconds;
                long snapshot = done;
                OnUi(() =>
                {
                    job.BytesDone = snapshot;
                    job.Current = current;
                    job.SpeedText = $"{Models.FileItem.FormatSize((long)speed)}/sn";
                    job.EtaText = speed > 1 ? FormatEta((job.BytesTotal - snapshot) / speed) : "…";
                    ProgressChanged?.Invoke(this, EventArgs.Empty);
                });
            }

            var copiedSources = new List<string>();
            await Task.Run(async () =>
            {
                foreach (var item in plan)
                {
                    token.ThrowIfCancellationRequested();
                    job.Gate.Wait(token);

                    if (item.IsDirectory)
                    {
                        Directory.CreateDirectory(item.Destination);
                        continue;
                    }

                    if (File.Exists(item.Destination) && !SameFile(item.Source, item.Destination))
                    {
                        var decision = Decide(job.Policy, item.Source, item.Destination);
                        if (decision is null)
                        {
                            lock (job.PendingConflicts) job.PendingConflicts.Add((item.Source, item.Destination, item.Size));
                            OnUi(() => job.ConflictCount = job.PendingConflicts.Count);
                            done += item.Size; // ilerleme çubuğu takılmasın; karar sonrası düzeltilir
                            continue;
                        }
                        if (!await ApplyAsync(job, item.Source, item.Destination, item.Size, decision.Value, b => { done += b; Report(item.Source); }, token).ConfigureAwait(false))
                            continue;
                    }
                    else
                    {
                        if (!await CopyOneAsync(job, item.Source, item.Destination, b => { done += b; Report(item.Source); }, token).ConfigureAwait(false))
                            continue;
                    }
                    copiedSources.Add(item.Source);
                    OnUi(() => job.FilesDone++);
                }
            }, token);
            Report(string.Empty, force: true);

            // 4) Çakışmalar: kuyruğu durdurmadan sona bırakılanlar için karar bekle
            if (job.PendingConflicts.Count > 0)
            {
                job.ConflictDecision = new TaskCompletionSource<ConflictPolicy>(TaskCreationOptions.RunContinuationsAsynchronously);
                job.State = TransferState.WaitingForConflicts;
                ConflictsWaiting?.Invoke(this, job);
                RaiseProgress();
                var decision = await job.ConflictDecision.Task;
                token.ThrowIfCancellationRequested();
                job.State = TransferState.Running;

                await Task.Run(async () =>
                {
                    foreach (var (source, destination, size) in job.PendingConflicts.ToList())
                    {
                        token.ThrowIfCancellationRequested();
                        job.Gate.Wait(token);
                        var effective = decision == ConflictPolicy.AskLater ? ConflictPolicy.Skip : decision;
                        done -= size;
                        if (await ApplyAsync(job, source, destination, size, effective, b => { done += b; Report(source); }, token).ConfigureAwait(false))
                        {
                            copiedSources.Add(source);
                            OnUi(() => job.FilesDone++);
                        }
                        else
                        {
                            done += size;
                        }
                    }
                }, token);
                job.PendingConflicts.Clear();
                job.ConflictCount = 0;
            }

            // 5) Taşımada: başarıyla kopyalanan kaynakları sil, boş kalan klasörleri kaldır
            if (job.Kind == TransferKind.Move)
            {
                await Task.Run(() => DeleteMovedSources(job, copiedSources), token);
            }

            Report(string.Empty, force: true);
            job.Current = string.Empty;
            job.State = job.Errors.Count > 0 ? TransferState.CompletedWithErrors : TransferState.Completed;
            job.ErrorCount = job.Errors.Count;
        }
        catch (OperationCanceledException)
        {
            job.State = TransferState.Canceled;
        }
        catch (Exception ex)
        {
            job.Errors.Add(ex.Message);
            job.ErrorCount = job.Errors.Count;
            job.State = TransferState.CompletedWithErrors;
            CrashLog.Write("Transfer", ex);
        }
    }

    private sealed record PlanItem(string Source, string Destination, long Size, bool IsDirectory);

    private static List<PlanItem> BuildPlan(TransferJob job, CancellationToken token)
    {
        var plan = new List<PlanItem>();
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = 0 };
        foreach (var source in job.Sources)
        {
            token.ThrowIfCancellationRequested();
            var name = Path.GetFileName(source.TrimEnd('\\'));
            var destination = Path.Combine(job.Target, name);
            if (IsSameOrInside(job.Target, source))
                throw new InvalidOperationException($"\"{name}\" kendi içine kopyalanamaz.");

            if (Directory.Exists(source))
            {
                plan.Add(new PlanItem(source, destination, 0, true));
                foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos("*", options))
                {
                    token.ThrowIfCancellationRequested();
                    var target = Path.Combine(destination, Path.GetRelativePath(source, entry.FullName));
                    plan.Add(entry is FileInfo f
                        ? new PlanItem(f.FullName, target, f.Length, false)
                        : new PlanItem(entry.FullName, target, 0, true));
                }
            }
            else if (File.Exists(source))
            {
                plan.Add(new PlanItem(source, destination, new FileInfo(source).Length, false));
            }
        }
        // Klasörler önce oluşturulsun.
        return plan.OrderBy(p => p.IsDirectory ? 0 : 1).ThenBy(p => p.Destination.Length).ToList();
    }

    private static bool IsSameOrInside(string target, string source)
    {
        var t = Path.GetFullPath(target).TrimEnd('\\') + "\\";
        var s = Path.GetFullPath(source).TrimEnd('\\') + "\\";
        return Directory.Exists(source) && t.StartsWith(s, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Aynı birimde, hiçbir çakışma yoksa taşımayı tek adımda yap.</summary>
    private static bool TryFastMove(TransferJob job, CancellationToken token)
    {
        var targetRoot = Path.GetPathRoot(Path.GetFullPath(job.Target));
        if (job.Sources.Any(s => !string.Equals(Path.GetPathRoot(Path.GetFullPath(s)), targetRoot, StringComparison.OrdinalIgnoreCase))) return false;
        foreach (var source in job.Sources)
        {
            var destination = Path.Combine(job.Target, Path.GetFileName(source.TrimEnd('\\')));
            if (File.Exists(destination) || Directory.Exists(destination)) return false;
        }
        foreach (var source in job.Sources)
        {
            token.ThrowIfCancellationRequested();
            var destination = Path.Combine(job.Target, Path.GetFileName(source.TrimEnd('\\')));
            if (Directory.Exists(source)) Directory.Move(source, destination);
            else File.Move(source, destination);
        }
        return true;
    }

    private static ConflictPolicy? Decide(ConflictPolicy policy, string source, string destination) => policy switch
    {
        ConflictPolicy.AskLater => null,
        _ => policy,
    };

    private async Task<bool> ApplyAsync(TransferJob job, string source, string destination, long size, ConflictPolicy decision, Action<long> progress, CancellationToken token)
    {
        switch (decision)
        {
            case ConflictPolicy.Skip:
                progress(size);
                return false;
            case ConflictPolicy.OverwriteIfNewer:
                if (File.GetLastWriteTimeUtc(source) <= File.GetLastWriteTimeUtc(destination).AddSeconds(2))
                {
                    progress(size);
                    return false;
                }
                return await CopyOneAsync(job, source, destination, progress, token);
            case ConflictPolicy.KeepBoth:
                return await CopyOneAsync(job, source, ShellNew.UniquePath(destination), progress, token);
            default:
                return await CopyOneAsync(job, source, destination, progress, token);
        }
    }

    private async Task<bool> CopyOneAsync(TransferJob job, string source, string destination, Action<long> progress, CancellationToken token)
    {
        var temp = destination + ".zfccopy";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous))
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
            {
                output.SetLength(input.Length);
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    job.Gate.Wait(token);
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    progress(read);
                }
            }

            var info = new FileInfo(source);
            File.SetCreationTimeUtc(temp, info.CreationTimeUtc);
            File.SetLastWriteTimeUtc(temp, info.LastWriteTimeUtc);
            if (File.Exists(destination)) File.SetAttributes(destination, FileAttributes.Normal); // salt okunur hedefin üzerine yazılabilsin
            File.Move(temp, destination, overwrite: true);
            try { File.SetAttributes(destination, info.Attributes); } catch { /* öznitelik kopyalanamazsa önemli değil */ }
            return true;
        }
        catch (OperationCanceledException)
        {
            TryDelete(temp);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(temp);
            lock (job.Errors) job.Errors.Add($"{Path.GetFileName(source)}: {ex.Message}");
            return false;
        }
    }

    private static void DeleteMovedSources(TransferJob job, List<string> copiedFiles)
    {
        foreach (var file in copiedFiles)
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch (Exception ex)
            {
                lock (job.Errors) job.Errors.Add($"Kaynak silinemedi: {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        // Tamamen boşalan kaynak klasörleri kaldır (içinde kalan dosya varsa dokunma).
        foreach (var source in job.Sources.Where(Directory.Exists))
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                }
                if (!Directory.EnumerateFileSystemEntries(source).Any()) Directory.Delete(source);
            }
            catch
            {
                // boş olmayan ya da kilitli klasör kalır
            }
        }
    }

    private static bool SameFile(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* yoksay */ }
    }

    private static string FormatEta(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) return "…";
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours} sa {span.Minutes} dk kaldı"
            : span.TotalMinutes >= 1 ? $"{span.Minutes} dk {span.Seconds} sn kaldı"
            : $"{Math.Max(1, span.Seconds)} sn kaldı";
    }

    private void RaiseProgress() => OnUi(() => ProgressChanged?.Invoke(this, EventArgs.Empty));

    private void OnUi(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess) action();
        else _dispatcher.TryEnqueue(() => action());
    }
}
