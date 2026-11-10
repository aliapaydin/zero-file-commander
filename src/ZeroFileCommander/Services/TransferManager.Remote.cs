using ZeroFileCommander.Services.Connections;

namespace ZeroFileCommander.Services;

/// <summary>Uzak bağlantılara (SFTP/FTP) yükleme ve indirme; yerel işlerle aynı kuyrukta, aynı denetimlerle.</summary>
public sealed partial class TransferManager
{
    private sealed record RemotePlanItem(string Source, string Destination, long Size, bool IsDirectory, DateTime? Modified, int Root);

    private async Task RunRemoteJobAsync(TransferJob job)
    {
        var token = job.Cancellation.Token;
        bool download = job.SourceConnection is not null;
        var connection = job.SourceConnection ?? job.TargetConnection!;
        job.State = TransferState.Scanning;
        RaiseProgress();

        IRemoteSession? session = null;
        try
        {
            // Aktarım kendi oturumunu kullanır; paneller bu sırada gezinmeye devam edebilir.
            session = await RemoteSessions.OpenAsync(connection, token);
            string target = download ? job.Target : RemoteTargetPath(job.Target);

            var plan = download
                ? await BuildDownloadPlanAsync(job, session, target, token)
                : await Task.Run(() => BuildUploadPlan(job, target, token), token);
            job.FilesTotal = plan.Count(p => !p.IsDirectory);
            job.BytesTotal = plan.Sum(p => p.Size);
            job.State = TransferState.Running;

            var reporter = new ProgressReporter(this, job);
            var failedRoots = new HashSet<int>();
            var remoteListings = new Dictionary<string, Dictionary<string, RemoteEntry>>(StringComparer.Ordinal);
            var deferred = new List<RemotePlanItem>();

            async Task<RemoteEntry?> ExistingRemoteAsync(string path)
            {
                var folder = RemotePath.Parent(path);
                if (!remoteListings.TryGetValue(folder, out var entries))
                {
                    try
                    {
                        entries = (await session!.ListAsync(folder, token)).ToDictionary(e => e.Name, StringComparer.Ordinal);
                    }
                    catch (DirectoryNotFoundException)
                    {
                        entries = new Dictionary<string, RemoteEntry>(StringComparer.Ordinal);
                    }
                    remoteListings[folder] = entries;
                }
                return entries.TryGetValue(RemotePath.Name(path), out var entry) ? entry : null;
            }

            async Task<bool> TransferAsync(RemotePlanItem item, ConflictPolicy? decision)
            {
                string destination = item.Destination;
                if (decision == ConflictPolicy.Skip)
                {
                    reporter.Add(item.Size, item.Source);
                    return false;
                }
                if (decision == ConflictPolicy.KeepBoth)
                {
                    destination = download ? ShellNew.UniquePath(destination) : await UniqueRemoteAsync(destination);
                }
                try
                {
                    // Akış işleri arka planda: duraklatma kapısı arayüzü kilitlemesin.
                    if (download) await Task.Run(() => DownloadOneAsync(job, session!, item, destination, reporter, token), token);
                    else await Task.Run(() => UploadOneAsync(job, session!, item, destination, reporter, token), token);
                    if (!download)
                    {
                        // Sonraki çakışma denetimleri için listeyi güncel tut.
                        var folder = RemotePath.Parent(destination);
                        if (remoteListings.TryGetValue(folder, out var entries))
                            entries[RemotePath.Name(destination)] = new RemoteEntry(RemotePath.Name(destination), destination, false, item.Size, DateTime.Now);
                    }
                    OnUi(() => job.FilesDone++);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lock (job.Errors) job.Errors.Add($"{Path.GetFileName(item.Source.TrimEnd('/', '\\'))}: {ex.Message}");
                    reporter.Add(Math.Max(0, item.Size - reporter.CurrentFileBytes), item.Source);
                    return false;
                }
            }

            async Task<string> UniqueRemoteAsync(string path)
            {
                string folder = RemotePath.Parent(path);
                string name = Path.GetFileNameWithoutExtension(RemotePath.Name(path));
                string extension = Path.GetExtension(path);
                for (int i = 2; i < 1000; i++)
                {
                    var candidate = RemotePath.Combine(folder, $"{name} ({i}){extension}");
                    if (await ExistingRemoteAsync(candidate) is null) return candidate;
                }
                return path;
            }

            foreach (var item in plan)
            {
                token.ThrowIfCancellationRequested();
                await WaitGateAsync(job, token);

                if (item.IsDirectory)
                {
                    try
                    {
                        if (download) Directory.CreateDirectory(item.Destination);
                        else if (await ExistingRemoteAsync(item.Destination) is null)
                        {
                            await session.CreateDirectoryAsync(item.Destination, token);
                            var folder = RemotePath.Parent(item.Destination);
                            if (remoteListings.TryGetValue(folder, out var entries))
                                entries[RemotePath.Name(item.Destination)] = new RemoteEntry(RemotePath.Name(item.Destination), item.Destination, true, 0, null);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failedRoots.Add(item.Root);
                        lock (job.Errors) job.Errors.Add($"Klasör oluşturulamadı: {item.Destination}: {ex.Message}");
                    }
                    continue;
                }

                // Çakışma: hedefte aynı adlı dosya var mı?
                DateTime? existingModified = null;
                bool exists;
                if (download)
                {
                    exists = File.Exists(item.Destination);
                    if (exists) existingModified = File.GetLastWriteTime(item.Destination);
                }
                else
                {
                    var existing = await ExistingRemoteAsync(item.Destination);
                    exists = existing is not null;
                    existingModified = existing?.Modified;
                }

                ConflictPolicy? decision = null;
                if (exists)
                {
                    decision = job.Policy switch
                    {
                        ConflictPolicy.AskLater => null,
                        ConflictPolicy.OverwriteIfNewer => IsNewer(item.Modified, existingModified) ? ConflictPolicy.Overwrite : ConflictPolicy.Skip,
                        _ => job.Policy,
                    };
                    if (decision is null)
                    {
                        deferred.Add(item);
                        OnUi(() => job.ConflictCount = deferred.Count);
                        reporter.Add(item.Size, item.Source); // karar sonrası düzeltilir
                        continue;
                    }
                }

                if (!await TransferAsync(item, decision ?? ConflictPolicy.Overwrite) || decision == ConflictPolicy.Skip)
                    failedRoots.Add(item.Root);
            }

            if (deferred.Count > 0)
            {
                job.ConflictDecision = new TaskCompletionSource<ConflictPolicy>(TaskCreationOptions.RunContinuationsAsynchronously);
                job.State = TransferState.WaitingForConflicts;
                ConflictsWaiting?.Invoke(this, job);
                RaiseProgress();
                var choice = await job.ConflictDecision.Task;
                token.ThrowIfCancellationRequested();
                job.State = TransferState.Running;

                foreach (var item in deferred)
                {
                    token.ThrowIfCancellationRequested();
                    await WaitGateAsync(job, token);
                    reporter.Add(-item.Size, item.Source);
                    var decision = choice switch
                    {
                        ConflictPolicy.AskLater => ConflictPolicy.Skip,
                        ConflictPolicy.OverwriteIfNewer => IsNewer(item.Modified,
                            download ? File.GetLastWriteTime(item.Destination) : (await ExistingRemoteAsync(item.Destination))?.Modified)
                            ? ConflictPolicy.Overwrite : ConflictPolicy.Skip,
                        _ => choice,
                    };
                    if (!await TransferAsync(item, decision) || decision == ConflictPolicy.Skip) failedRoots.Add(item.Root);
                }
                job.ConflictCount = 0;
            }

            // Taşıma: yalnızca tamamı sorunsuz aktarılan kökler silinir.
            if (job.Kind == TransferKind.Move)
            {
                for (int i = 0; i < job.Sources.Count; i++)
                {
                    if (failedRoots.Contains(i)) continue;
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if (download)
                        {
                            if (RemotePath.TryParse(job.Sources[i], out _, out var remote))
                            {
                                var root = plan.FirstOrDefault(p => p.Root == i);
                                if (root is not null) await session.DeleteAsync(remote, root.IsDirectory && root.Source == remote, token);
                            }
                        }
                        else
                        {
                            // Yerel kaynak Geri Dönüşüm Kutusu'na: sunucuda bir sorun olursa geri alınabilsin.
                            await ShellFileOperations.RecycleAsync(IntPtr.Zero, new[] { job.Sources[i] });
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        lock (job.Errors) job.Errors.Add($"Kaynak silinemedi: {Path.GetFileName(job.Sources[i].TrimEnd('/', '\\'))}: {ex.Message}");
                    }
                }
            }

            reporter.Flush();
            job.Current = string.Empty;
            job.ErrorCount = job.Errors.Count;
            job.State = job.Errors.Count > 0 ? TransferState.CompletedWithErrors : TransferState.Completed;
        }
        catch (OperationCanceledException)
        {
            job.State = TransferState.Canceled;
        }
        catch (Exception ex)
        {
            lock (job.Errors) job.Errors.Add(ex switch
            {
                RemoteAuthException => "Oturum açılamadı: " + ex.Message,
                RemoteHostKeyException => "Sunucu anahtarı doğrulanmadı. Bağlantıyı önce panelde açıp anahtarı onaylayın.",
                _ => ex.Message,
            });
            job.ErrorCount = job.Errors.Count;
            job.State = TransferState.CompletedWithErrors;
            CrashLog.Write("Uzak aktarım", ex);
        }
        finally
        {
            session?.Dispose();
        }
    }

    private static Task WaitGateAsync(TransferJob job, CancellationToken token) =>
        job.Gate.IsSet ? Task.CompletedTask : Task.Run(() => job.Gate.Wait(token), token);

    private static bool IsNewer(DateTime? source, DateTime? destination) =>
        source is null || destination is null || source.Value > destination.Value.AddSeconds(2);

    private static string RemoteTargetPath(string uri) =>
        RemotePath.TryParse(uri, out _, out var path) ? path : throw new InvalidOperationException("Hedef bağlantı bulunamadı: " + uri);

    private static async Task<List<RemotePlanItem>> BuildDownloadPlanAsync(TransferJob job, IRemoteSession session, string localTarget, CancellationToken token)
    {
        var plan = new List<RemotePlanItem>();
        for (int i = 0; i < job.Sources.Count; i++)
        {
            if (!RemotePath.TryParse(job.Sources[i], out _, out var remote)) continue;
            var entry = await session.StatAsync(remote, token);
            if (entry is null)
            {
                lock (job.Errors) job.Errors.Add($"Bulunamadı: {remote}");
                continue;
            }
            var destination = Path.Combine(localTarget, SafeLocalName(entry.Name));
            if (!entry.IsDirectory)
            {
                plan.Add(new RemotePlanItem(remote, destination, entry.Size, false, entry.Modified, i));
                continue;
            }
            plan.Add(new RemotePlanItem(remote, destination, 0, true, entry.Modified, i));
            var pending = new Queue<(string Remote, string Local)>();
            pending.Enqueue((remote, destination));
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var (folder, local) = pending.Dequeue();
                foreach (var child in await session.ListAsync(folder, token))
                {
                    var childLocal = Path.Combine(local, SafeLocalName(child.Name));
                    plan.Add(new RemotePlanItem(child.Path, childLocal, child.Size, child.IsDirectory, child.Modified, i));
                    if (child.IsDirectory) pending.Enqueue((child.Path, childLocal));
                }
            }
        }
        return plan;
    }

    private static List<RemotePlanItem> BuildUploadPlan(TransferJob job, string remoteTarget, CancellationToken token)
    {
        var plan = new List<RemotePlanItem>();
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = 0 };
        for (int i = 0; i < job.Sources.Count; i++)
        {
            var source = job.Sources[i];
            var destination = RemotePath.Combine(remoteTarget, Path.GetFileName(source.TrimEnd('\\')));
            if (Directory.Exists(source))
            {
                plan.Add(new RemotePlanItem(source, destination, 0, true, null, i));
                foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos("*", options).OrderBy(e => e.FullName.Length))
                {
                    token.ThrowIfCancellationRequested();
                    var remote = RemotePath.Combine(destination, Path.GetRelativePath(source, entry.FullName).Replace('\\', '/'));
                    plan.Add(entry is FileInfo f
                        ? new RemotePlanItem(f.FullName, remote, f.Length, false, f.LastWriteTime, i)
                        : new RemotePlanItem(entry.FullName, remote, 0, true, null, i));
                }
            }
            else if (File.Exists(source))
            {
                var info = new FileInfo(source);
                plan.Add(new RemotePlanItem(source, destination, info.Length, false, info.LastWriteTime, i));
            }
        }
        // Klasörler önce, üstten alta.
        return plan.OrderBy(p => p.IsDirectory ? 0 : 1).ThenBy(p => p.Destination.Length).ToList();
    }

    /// <summary>Windows'ta geçersiz karakterleri (: * ? " &lt; &gt; |) değiştirir; Unix sunucularında bunlar serbesttir.</summary>
    private static string SafeLocalName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var safe = new string(chars).TrimEnd('.', ' ');
        return safe.Length == 0 ? "_" : safe;
    }

    private static async Task DownloadOneAsync(TransferJob job, IRemoteSession session, RemotePlanItem item, string destination, ProgressReporter reporter, CancellationToken token)
    {
        var temp = destination + ".zfccopy";
        reporter.BeginFile(item.Source);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
            await using (var counting = new ProgressStream(file, job, reporter, token))
            {
                await session.DownloadAsync(item.Source, counting, token);
            }
            if (item.Modified is { } modified) File.SetLastWriteTime(temp, modified);
            if (File.Exists(destination)) File.SetAttributes(destination, FileAttributes.Normal);
            File.Move(temp, destination, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
            throw;
        }
    }

    private static async Task UploadOneAsync(TransferJob job, IRemoteSession session, RemotePlanItem item, string destination, ProgressReporter reporter, CancellationToken token)
    {
        reporter.BeginFile(item.Source);
        await using var file = new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var counting = new ProgressStream(file, job, reporter, token);
        await session.UploadAsync(counting, destination, token);
    }

    /// <summary>Bayt sayar, ilerlemeyi ~4/sn bildirir, hız ve kalan süreyi hesaplar.</summary>
    private sealed class ProgressReporter
    {
        private readonly TransferManager _owner;
        private readonly TransferJob _job;
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private readonly Queue<(long Ms, long Bytes)> _samples = new();
        private long _done;
        private long _lastReport;
        private string _current = string.Empty;

        public ProgressReporter(TransferManager owner, TransferJob job)
        {
            _owner = owner;
            _job = job;
        }

        /// <summary>Şu anki dosyada aktarılan bayt (hata olursa kalanı eklemek için).</summary>
        public long CurrentFileBytes { get; private set; }

        public void BeginFile(string name)
        {
            CurrentFileBytes = 0;
            _current = name;
        }

        public void AddFileBytes(int count)
        {
            CurrentFileBytes += count;
            Add(count, _current);
        }

        public void Add(long bytes, string current)
        {
            Interlocked.Add(ref _done, bytes);
            _current = current;
            Report(false);
        }

        public void Flush() => Report(true);

        private void Report(bool force)
        {
            long now = _clock.ElapsedMilliseconds;
            if (!force && now - _lastReport < 250) return;
            _lastReport = now;
            long done = Interlocked.Read(ref _done);
            _samples.Enqueue((now, done));
            while (_samples.Count > 1 && now - _samples.Peek().Ms > 3000) _samples.Dequeue();
            var first = _samples.Peek();
            double seconds = Math.Max(0.25, (now - first.Ms) / 1000.0);
            double speed = Math.Max(0, (done - first.Bytes) / seconds);
            string current = _current;
            _owner.OnUi(() =>
            {
                _job.BytesDone = Math.Clamp(done, 0, Math.Max(_job.BytesTotal, done));
                _job.Current = current;
                _job.SpeedText = $"{Models.FileItem.FormatSize((long)speed)}/sn";
                _job.EtaText = speed > 1 ? FormatEta((_job.BytesTotal - done) / speed) : "…";
                _owner.ProgressChanged?.Invoke(_owner, EventArgs.Empty);
            });
        }
    }

    /// <summary>Kütüphanelerin okuduğu/yazdığı akışı sarar: ilerleme, duraklatma ve iptal buradan işler.</summary>
    private sealed class ProgressStream : Stream
    {
        private readonly Stream _inner;
        private readonly TransferJob _job;
        private readonly ProgressReporter _reporter;
        private readonly CancellationToken _token;

        public ProgressStream(Stream inner, TransferJob job, ProgressReporter reporter, CancellationToken token)
        {
            _inner = inner;
            _job = job;
            _reporter = reporter;
            _token = token;
        }

        private void Checkpoint()
        {
            _token.ThrowIfCancellationRequested();
            _job.Gate.Wait(_token);
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);

        public override int Read(byte[] buffer, int offset, int count)
        {
            Checkpoint();
            int read = _inner.Read(buffer, offset, count);
            if (read > 0) _reporter.AddFileBytes(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Checkpoint();
            int read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0) _reporter.AddFileBytes(read);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Write(byte[] buffer, int offset, int count)
        {
            Checkpoint();
            _inner.Write(buffer, offset, count);
            _reporter.AddFileBytes(count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Checkpoint();
            await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            _reporter.AddFileBytes(buffer.Length);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        // İç akışı çağıran kapatır.
        protected override void Dispose(bool disposing) { }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
