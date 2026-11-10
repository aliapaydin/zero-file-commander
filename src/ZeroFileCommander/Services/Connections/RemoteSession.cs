using System.Collections.Concurrent;
using System.Net.Security;
using FluentFTP;
using FluentFTP.Exceptions;
using Renci.SshNet;

namespace ZeroFileCommander.Services.Connections;

/// <summary>Uzak sunucudaki bir dosya ya da klasör. Yollar POSIX biçimindedir ("/a/b").</summary>
public sealed record RemoteEntry(string Name, string Path, bool IsDirectory, long Size, DateTime? Modified);

/// <summary>Sunucu anahtarı ilk kez görülüyor ya da kayıtlı olandan farklı.</summary>
public sealed class RemoteHostKeyException : Exception
{
    public RemoteHostKeyException(string fingerprint, string algorithm, bool mismatch)
        : base(mismatch ? "Sunucu anahtarı değişmiş" : "Sunucu anahtarı doğrulanmadı")
    {
        Fingerprint = fingerprint;
        Algorithm = algorithm;
        IsMismatch = mismatch;
    }

    public string Fingerprint { get; }
    public string Algorithm { get; }
    public bool IsMismatch { get; }
}

/// <summary>Kullanıcı adı/parola ya da anahtar reddedildi.</summary>
public sealed class RemoteAuthException : Exception
{
    public RemoteAuthException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Bir sunucuyla açık oturum. İşlemler sırayla yürür (FTP kontrol kanalı tektir).</summary>
public interface IRemoteSession : IDisposable
{
    SavedConnection Connection { get; }
    Task<string> GetHomeAsync(CancellationToken token);
    Task<List<RemoteEntry>> ListAsync(string path, CancellationToken token);
    Task<RemoteEntry?> StatAsync(string path, CancellationToken token);
    Task DownloadAsync(string path, Stream destination, CancellationToken token);
    Task UploadAsync(Stream source, string path, CancellationToken token);
    Task CreateDirectoryAsync(string path, CancellationToken token);
    Task DeleteAsync(string path, bool isDirectory, CancellationToken token);
    Task RenameAsync(string from, string to, CancellationToken token);
}

public static class RemotePath
{
    public static string Combine(string folder, string name) =>
        folder.EndsWith('/') ? folder + name : folder + "/" + name;

    public static string Parent(string path)
    {
        path = path.TrimEnd('/');
        int slash = path.LastIndexOf('/');
        return slash <= 0 ? "/" : path[..slash];
    }

    public static string Name(string path)
    {
        path = path.TrimEnd('/');
        int slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";
        path = path.Replace('\\', '/');
        if (!path.StartsWith('/')) path = "/" + path;
        var parts = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(part);
        }
        return "/" + string.Join('/', parts);
    }

    /// <summary>Panelde görünen tam adres: sftp://kullanici@sunucu/yol</summary>
    public static string ToUri(SavedConnection connection, string path) =>
        connection.RootUri + (Normalize(path) == "/" ? "/" : Normalize(path));

    /// <summary>Adresi kayıtlı bağlantıya ve uzak yola ayırır.</summary>
    public static bool TryParse(string uri, out SavedConnection connection, out string path)
    {
        connection = null!;
        path = "/";
        if (!uri.Contains("://", StringComparison.Ordinal)) return false;
        var found = ConnectionStore.FindByUri(uri);
        if (found is null) return false;
        connection = found;
        string rest = uri.Length > found.RootUri.Length ? uri[found.RootUri.Length..] : "/";
        path = Normalize(Uri.UnescapeDataString(rest));
        return true;
    }

    public static bool LooksRemote(string path) =>
        path.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Oturumları yönetir: paneller bağlantı başına bir oturumu paylaşır; aktarımlar kendi oturumunu açar.</summary>
public static class RemoteSessions
{
    private static readonly ConcurrentDictionary<string, Task<IRemoteSession>> Browsing = new();

    /// <summary>Gezinme oturumu (paylaşılan, bağlıysa yeniden kullanılır).</summary>
    public static async Task<IRemoteSession> GetAsync(SavedConnection connection, CancellationToken token = default)
    {
        while (true)
        {
            var task = Browsing.GetOrAdd(connection.Id, _ => OpenAsync(connection, CancellationToken.None));
            try
            {
                var session = await task.WaitAsync(token);
                if (session.Connection.HostKey == connection.HostKey && session.Connection.User == connection.User &&
                    session.Connection.Host == connection.Host && session.Connection.Port == connection.Port &&
                    session.Connection.KeyFile == connection.KeyFile && session.Connection.Kind == connection.Kind)
                    return session;
                // Bağlantı ayarları değişmiş: eski oturumu kapat, yenisini aç.
                Drop(connection.Id, task);
                session.Dispose();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                Drop(connection.Id, task); // başarısız denemeyi önbellekte bırakma
                throw;
            }
        }
    }

    /// <summary>Ayrı bir oturum (aktarımlar için); çağıran kapatır.</summary>
    public static Task<IRemoteSession> OpenAsync(SavedConnection connection, CancellationToken token) =>
        connection.Kind == ConnectionKind.Sftp
            ? SftpSession.ConnectAsync(connection.Clone(), token)
            : FtpSession.ConnectAsync(connection.Clone(), token);

    public static void Close(string connectionId)
    {
        if (Browsing.TryRemove(connectionId, out var task) && task.IsCompletedSuccessfully)
            task.Result.Dispose();
    }

    public static void CloseAll()
    {
        foreach (var id in Browsing.Keys.ToList()) Close(id);
    }

    private static void Drop(string id, Task<IRemoteSession> task) =>
        Browsing.TryRemove(new KeyValuePair<string, Task<IRemoteSession>>(id, task));
}

// ==================================================================== SFTP (SSH.NET)

internal sealed class SftpSession : IRemoteSession
{
    private readonly SftpClient _client;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private SftpSession(SavedConnection connection, SftpClient client)
    {
        Connection = connection;
        _client = client;
    }

    public SavedConnection Connection { get; }

    public static async Task<IRemoteSession> ConnectAsync(SavedConnection connection, CancellationToken token)
    {
        var client = CreateClient(connection);
        string? seenKey = null, seenAlgorithm = null;
        client.HostKeyReceived += (_, e) =>
        {
            seenKey = "SHA256:" + e.FingerPrintSHA256;
            seenAlgorithm = e.HostKeyName;
            e.CanTrust = connection.HostKey is not null && string.Equals(connection.HostKey, seenKey, StringComparison.Ordinal);
        };
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(20);
        client.OperationTimeout = TimeSpan.FromMinutes(2);
        client.KeepAliveInterval = TimeSpan.FromSeconds(30);

        try
        {
            await Task.Run(() => client.Connect(), token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            client.Dispose();
            if (seenKey is not null && !string.Equals(connection.HostKey, seenKey, StringComparison.Ordinal))
                throw new RemoteHostKeyException(seenKey, seenAlgorithm ?? "?", mismatch: connection.HostKey is not null);
            if (ex is Renci.SshNet.Common.SshAuthenticationException)
                throw new RemoteAuthException("Kullanıcı adı, parola ya da anahtar kabul edilmedi.", ex);
            throw new IOException(Friendly(ex), ex);
        }
        return new SftpSession(connection, client);
    }

    private static SftpClient CreateClient(SavedConnection connection)
    {
        string password = ConnectionStore.GetPassword(connection) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(connection.KeyFile))
        {
            if (!File.Exists(connection.KeyFile)) throw new FileNotFoundException("Anahtar dosyası bulunamadı: " + connection.KeyFile);
            try
            {
                // Parola alanı anahtar parolası olarak da kullanılır.
                var key = password.Length > 0 ? new PrivateKeyFile(connection.KeyFile, password) : new PrivateKeyFile(connection.KeyFile);
                return new SftpClient(connection.Host, connection.EffectivePort, connection.User, key);
            }
            catch (Renci.SshNet.Common.SshException ex)
            {
                throw new RemoteAuthException("Anahtar dosyası açılamadı (parolası yanlış ya da biçimi desteklenmiyor).", ex);
            }
        }
        return new SftpClient(connection.Host, connection.EffectivePort, connection.User, password);
    }

    private static string Friendly(Exception ex) => ex switch
    {
        System.Net.Sockets.SocketException s => $"Sunucuya ulaşılamadı ({s.SocketErrorCode}).",
        Renci.SshNet.Common.SshOperationTimeoutException => "Sunucu zamanında yanıt vermedi.",
        _ => ex.Message,
    };

    private async Task<T> RunAsync<T>(Func<T> action, CancellationToken token)
    {
        await _lock.WaitAsync(token);
        try
        {
            return await Task.Run(() =>
            {
                if (!_client.IsConnected) _client.Connect(); // uyku/ağ kesintisi sonrası
                return action();
            }, token);
        }
        catch (Renci.SshNet.Common.SftpPathNotFoundException ex)
        {
            throw new DirectoryNotFoundException("Uzak yol bulunamadı.", ex);
        }
        catch (Renci.SshNet.Common.SftpPermissionDeniedException ex)
        {
            throw new UnauthorizedAccessException("Sunucu bu işleme izin vermedi.", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task RunAsync(Action action, CancellationToken token) => RunAsync(() => { action(); return true; }, token);

    public Task<string> GetHomeAsync(CancellationToken token) => RunAsync(() => _client.WorkingDirectory, token);

    public Task<List<RemoteEntry>> ListAsync(string path, CancellationToken token) => RunAsync(() =>
    {
        var list = new List<RemoteEntry>();
        foreach (var file in _client.ListDirectory(path))
        {
            if (file.Name is "." or "..") continue;
            bool isDirectory = file.IsDirectory;
            if (file.IsSymbolicLink)
            {
                try
                {
                    isDirectory = _client.GetAttributes(file.FullName).IsDirectory;
                }
                catch (Renci.SshNet.Common.SshException)
                {
                    // kırık bağlantı: dosya gibi göster
                }
            }
            list.Add(new RemoteEntry(file.Name, file.FullName, isDirectory, isDirectory ? 0 : file.Length, file.LastWriteTime));
        }
        return list;
    }, token);

    public Task<RemoteEntry?> StatAsync(string path, CancellationToken token) => RunAsync<RemoteEntry?>(() =>
    {
        if (!_client.Exists(path)) return null;
        var attributes = _client.GetAttributes(path);
        return new RemoteEntry(RemotePath.Name(path), path, attributes.IsDirectory, attributes.IsDirectory ? 0 : attributes.Size, attributes.LastWriteTime);
    }, token);

    public Task DownloadAsync(string path, Stream destination, CancellationToken token) =>
        RunAsync(() => _client.DownloadFile(path, destination), token);

    public Task UploadAsync(Stream source, string path, CancellationToken token) =>
        RunAsync(() => _client.UploadFile(source, path, true), token);

    public Task CreateDirectoryAsync(string path, CancellationToken token) =>
        RunAsync(() => _client.CreateDirectory(path), token);

    public Task DeleteAsync(string path, bool isDirectory, CancellationToken token) => RunAsync(() =>
    {
        if (isDirectory) DeleteTree(path, token);
        else _client.DeleteFile(path);
    }, token);

    private void DeleteTree(string path, CancellationToken token)
    {
        foreach (var file in _client.ListDirectory(path))
        {
            token.ThrowIfCancellationRequested();
            if (file.Name is "." or "..") continue;
            if (file.IsDirectory && !file.IsSymbolicLink) DeleteTree(file.FullName, token);
            else _client.DeleteFile(file.FullName);
        }
        _client.DeleteDirectory(path);
    }

    public Task RenameAsync(string from, string to, CancellationToken token) =>
        RunAsync(() => _client.RenameFile(from, to), token);

    public void Dispose()
    {
        try
        {
            if (_client.IsConnected) _client.Disconnect();
        }
        catch (Exception)
        {
            // bağlantı zaten kopmuş olabilir
        }
        _client.Dispose();
        _lock.Dispose();
    }
}

// ==================================================================== FTP / FTPS (FluentFTP)

internal sealed class FtpSession : IRemoteSession
{
    private readonly AsyncFtpClient _client;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private FtpSession(SavedConnection connection, AsyncFtpClient client)
    {
        Connection = connection;
        _client = client;
    }

    public SavedConnection Connection { get; }

    public static async Task<IRemoteSession> ConnectAsync(SavedConnection connection, CancellationToken token)
    {
        string user = connection.User.Length > 0 ? connection.User : "anonymous";
        string password = ConnectionStore.GetPassword(connection) ?? string.Empty;
        var client = new AsyncFtpClient(connection.Host, user, password, connection.EffectivePort);
        client.Config.ConnectTimeout = 20000;
        client.Config.ReadTimeout = 60000;
        client.Config.DataConnectionConnectTimeout = 20000;
        client.Config.DataConnectionReadTimeout = 60000;
        if (connection.Kind == ConnectionKind.Ftps)
        {
            client.Config.EncryptionMode = FtpEncryptionMode.Explicit;
            client.Config.DataConnectionEncryption = true;
            // Yalnızca geçerli sertifikalara güven (kendinden imzalı sertifika reddedilir).
            client.ValidateCertificate += (_, e) => e.Accept = e.PolicyErrors == SslPolicyErrors.None;
        }
        else
        {
            client.Config.EncryptionMode = FtpEncryptionMode.None;
        }

        try
        {
            await client.Connect(token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await client.DisposeAsync();
            if (ex is FtpAuthenticationException)
                throw new RemoteAuthException("Kullanıcı adı ya da parola kabul edilmedi.", ex);
            if (ex is System.Security.Authentication.AuthenticationException)
                throw new IOException("Sunucunun TLS sertifikası doğrulanamadı.", ex);
            throw new IOException(ex is System.Net.Sockets.SocketException s ? $"Sunucuya ulaşılamadı ({s.SocketErrorCode})." : ex.Message, ex);
        }
        return new FtpSession(connection, client);
    }

    private async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        await _lock.WaitAsync(token);
        try
        {
            if (!_client.IsConnected) await _client.Connect(token);
            return await action();
        }
        catch (FtpCommandException ex) when (ex.CompletionCode is "550")
        {
            throw new IOException("Sunucu isteği reddetti: " + ex.Message, ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task RunAsync(Func<Task> action, CancellationToken token) => RunAsync(async () => { await action(); return true; }, token);

    public Task<string> GetHomeAsync(CancellationToken token) => RunAsync(() => _client.GetWorkingDirectory(token), token);

    public Task<List<RemoteEntry>> ListAsync(string path, CancellationToken token) => RunAsync(async () =>
    {
        var items = await _client.GetListing(path, token);
        return items
            .Where(i => i.Name is not ("." or ".."))
            .Select(i =>
            {
                bool isDirectory = i.Type == FtpObjectType.Directory ||
                                   (i.Type == FtpObjectType.Link && i.LinkObject?.Type == FtpObjectType.Directory);
                DateTime? modified = i.Modified == DateTime.MinValue ? null : i.Modified;
                return new RemoteEntry(i.Name, RemotePath.Combine(path, i.Name), isDirectory, isDirectory ? 0 : Math.Max(0, i.Size), modified);
            })
            .ToList();
    }, token);

    public async Task<RemoteEntry?> StatAsync(string path, CancellationToken token)
    {
        if (RemotePath.Normalize(path) == "/") return new RemoteEntry("/", "/", true, 0, null);
        var listing = await ListAsync(RemotePath.Parent(path), token);
        var name = RemotePath.Name(path);
        return listing.FirstOrDefault(e => e.Name == name);
    }

    public Task DownloadAsync(string path, Stream destination, CancellationToken token) => RunAsync(async () =>
    {
        if (!await _client.DownloadStream(destination, path, 0, null, token))
            throw new IOException("İndirme tamamlanamadı: " + path);
    }, token);

    public Task UploadAsync(Stream source, string path, CancellationToken token) => RunAsync(async () =>
    {
        var status = await _client.UploadStream(source, path, FtpRemoteExists.Overwrite, false, null, token);
        if (status == FtpStatus.Failed) throw new IOException("Yükleme tamamlanamadı: " + path);
    }, token);

    public Task CreateDirectoryAsync(string path, CancellationToken token) =>
        RunAsync(async () => { await _client.CreateDirectory(path, token); }, token);

    public Task DeleteAsync(string path, bool isDirectory, CancellationToken token) => RunAsync(async () =>
    {
        if (isDirectory) await _client.DeleteDirectory(path, token); // içeriğiyle birlikte
        else await _client.DeleteFile(path, token);
    }, token);

    public Task RenameAsync(string from, string to, CancellationToken token) =>
        RunAsync(async () => { await _client.Rename(from, to, token); }, token);

    public void Dispose()
    {
        // Kontrol kanalını arka planda kapat; arayüzü bekletme.
        var client = _client;
        _ = Task.Run(async () =>
        {
            try
            {
                if (client.IsConnected) await client.Disconnect();
            }
            catch (Exception)
            {
            }
            await client.DisposeAsync();
        });
        _lock.Dispose();
    }
}
