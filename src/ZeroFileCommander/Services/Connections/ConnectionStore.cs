using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Security.Credentials;

namespace ZeroFileCommander.Services.Connections;

public enum ConnectionKind
{
    /// <summary>Windows ağ paylaşımı / NAS (\\sunucu\paylaşım).</summary>
    Smb,
    Sftp,
    /// <summary>Şifrelenmemiş FTP.</summary>
    Ftp,
    /// <summary>Açık TLS ile FTP (FTPES).</summary>
    Ftps,
}

/// <summary>Kayıtlı bir bağlantı. Parola burada değil, Windows Kimlik Bilgisi Kasası'nda tutulur.</summary>
public sealed class SavedConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public ConnectionKind Kind { get; set; } = ConnectionKind.Sftp;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string User { get; set; } = string.Empty;

    /// <summary>Başlangıç klasörü. SMB'de paylaşım ve alt klasör ("paylasim\klasor").</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>SFTP için isteğe bağlı özel anahtar dosyası.</summary>
    public string? KeyFile { get; set; }

    /// <summary>SFTP sunucusunun onaylanmış anahtar parmak izi (SHA256). İlk bağlantıda kullanıcı onaylar.</summary>
    public string? HostKey { get; set; }

    [JsonIgnore]
    public bool IsRemote => Kind != ConnectionKind.Smb;

    [JsonIgnore]
    public int EffectivePort => Port > 0 ? Port : Kind switch
    {
        ConnectionKind.Sftp => 22,
        ConnectionKind.Ftp or ConnectionKind.Ftps => 21,
        _ => 445,
    };

    [JsonIgnore]
    public string Scheme => Kind switch
    {
        ConnectionKind.Sftp => "sftp",
        ConnectionKind.Ftps => "ftps",
        ConnectionKind.Ftp => "ftp",
        _ => "smb",
    };

    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        ConnectionKind.Sftp => "SFTP",
        ConnectionKind.Ftps => "FTPS",
        ConnectionKind.Ftp => "FTP",
        _ => "Ağ paylaşımı",
    };

    [JsonIgnore]
    public string Glyph => Kind == ConnectionKind.Smb ? "\uE8CE" : "\uE968";

    /// <summary>SMB için tam UNC yolu.</summary>
    [JsonIgnore]
    public string UncPath
    {
        get
        {
            var host = Host.Trim().TrimStart('\\').TrimEnd('\\');
            var path = Path.Trim().Replace('/', '\\').Trim('\\');
            return path.Length > 0 ? $@"\\{host}\{path}" : $@"\\{host}";
        }
    }

    /// <summary>UNC yolunun paylaşım kökü (\\sunucu\paylaşım) — kimlik bilgisi buna bağlanır.</summary>
    [JsonIgnore]
    public string ShareRoot
    {
        get
        {
            var parts = UncPath.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}" : UncPath;
        }
    }

    /// <summary>Sağlam bağlantı adresi (panelde, geçmişte ve sekmelerde görünür).</summary>
    [JsonIgnore]
    public string RootUri
    {
        get
        {
            string user = User.Length > 0 ? Uri.EscapeDataString(User) + "@" : string.Empty;
            string port = Port > 0 && Port != EffectivePortDefault ? $":{Port}" : string.Empty;
            return $"{Scheme}://{user}{Host}{port}";
        }
    }

    private int EffectivePortDefault => Kind == ConnectionKind.Sftp ? 22 : 21;

    public SavedConnection Clone() => (SavedConnection)MemberwiseClone();

    public override string ToString() => Name;
}

/// <summary>Bağlantıları JSON olarak (parolasız) saklar; parolalar PasswordVault'tadır.</summary>
public static class ConnectionStore
{
    private const string VaultResource = "ZeroFileCommander.Connection";
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static List<SavedConnection>? _all;

    public static event EventHandler? Changed;

    private static string FilePath
    {
        get
        {
            string folder;
            try
            {
                folder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            }
            catch (InvalidOperationException)
            {
                folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZeroFileCommander");
                Directory.CreateDirectory(folder);
            }
            return System.IO.Path.Combine(folder, "connections.json");
        }
    }

    public static IReadOnlyList<SavedConnection> All => _all ??= Load();

    public static SavedConnection? Find(string id) => All.FirstOrDefault(c => c.Id == id);

    /// <summary>"sftp://kullanici@sunucu:2222/yol" biçimindeki adrese uyan kayıt.</summary>
    public static SavedConnection? FindByUri(string uri)
    {
        foreach (var connection in All.Where(c => c.IsRemote))
        {
            var root = connection.RootUri;
            if (uri.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                uri.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                return connection;
        }
        return null;
    }

    public static void Save(SavedConnection connection)
    {
        var list = All.ToList();
        int index = list.FindIndex(c => c.Id == connection.Id);
        if (index >= 0) list[index] = connection;
        else list.Add(connection);
        _all = list;
        Persist();
    }

    public static void Remove(SavedConnection connection)
    {
        _all = All.Where(c => c.Id != connection.Id).ToList();
        SetPassword(connection, null);
        Persist();
    }

    public static string? GetPassword(SavedConnection connection)
    {
        try
        {
            var credential = new PasswordVault().Retrieve(VaultResource, connection.Id);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (Exception)
        {
            return null; // kayıt yok
        }
    }

    /// <summary>Parolayı kasaya yazar; null ya da boşsa siler.</summary>
    public static void SetPassword(SavedConnection connection, string? password)
    {
        var vault = new PasswordVault();
        try
        {
            vault.Remove(vault.Retrieve(VaultResource, connection.Id));
        }
        catch (Exception)
        {
            // yoktu
        }
        if (!string.IsNullOrEmpty(password))
            vault.Add(new PasswordCredential(VaultResource, connection.Id, password));
    }

    private static List<SavedConnection> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<SavedConnection>>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            CrashLog.Write("Bağlantılar okunamadı", ex);
        }
        return new();
    }

    private static void Persist()
    {
        try
        {
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_all, Json));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("Bağlantılar kaydedilemedi", ex);
        }
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
