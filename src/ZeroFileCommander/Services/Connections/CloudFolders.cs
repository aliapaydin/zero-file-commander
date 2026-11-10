using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace ZeroFileCommander.Services.Connections;

/// <summary>Eşitlenen bir bulut klasörü (OneDrive, Google Drive, Dropbox, iCloud, Box…).</summary>
public sealed record CloudFolder(string Name, string Path);

/// <summary>
/// Windows'a kayıtlı bulut eşitleme köklerini bulur (SyncRootManager kayıt defteri);
/// eski sürümler için bilinen klasörlere de bakar.
/// </summary>
public static class CloudFolders
{
    private static List<CloudFolder>? _cache;
    private static DateTime _cacheTime;

    public static IReadOnlyList<CloudFolder> Get()
    {
        if (_cache is not null && DateTime.UtcNow - _cacheTime < TimeSpan.FromSeconds(30)) return _cache;
        var found = new List<CloudFolder>();
        try
        {
            ReadSyncRoots(found);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
        AddKnown(found);
        _cache = found
            .Where(f => Directory.Exists(f.Path))
            .GroupBy(f => f.Path.TrimEnd('\\'), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _cacheTime = DateTime.UtcNow;
        return _cache;
    }

    /// <summary>Yol bir bulut klasörünün içinde mi.</summary>
    public static bool Contains(string path) =>
        Get().Any(f => path.StartsWith(f.Path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ||
                       path.TrimEnd('\\').Equals(f.Path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

    private static void ReadSyncRoots(List<CloudFolder> found)
    {
        string? sid = WindowsIdentity.GetCurrent().User?.Value;
        if (sid is null) return;
        using var manager = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager");
        if (manager is null) return;
        foreach (var keyName in manager.GetSubKeyNames())
        {
            using var key = manager.OpenSubKey(keyName);
            using var roots = key?.OpenSubKey("UserSyncRoots");
            if (key is null || roots is null) continue;
            if (roots.GetValue(sid) is not string path || path.Length == 0) continue;

            string provider = keyName.Split('!')[0];
            string name = key.GetValue("DisplayNameResource") is string resource ? LoadIndirect(resource) ?? provider : provider;
            // OneDrive iş/kişisel hesapları aynı adı taşır; klasör adını ekleyerek ayırt et.
            string folderName = System.IO.Path.GetFileName(path.TrimEnd('\\'));
            if (!folderName.Contains(name, StringComparison.OrdinalIgnoreCase) && !name.Contains(folderName, StringComparison.OrdinalIgnoreCase))
                name = $"{name} · {folderName}";
            else if (folderName.Length > name.Length)
                name = folderName;
            found.Add(new CloudFolder(name, path));
        }
    }

    private static void AddKnown(List<CloudFolder> found)
    {
        void TryAdd(string name, string? path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) found.Add(new CloudFolder(name, path));
        }

        TryAdd("OneDrive", Environment.GetEnvironmentVariable("OneDriveConsumer"));
        TryAdd("OneDrive (iş)", Environment.GetEnvironmentVariable("OneDriveCommercial"));
        if (found.Count == 0) TryAdd("OneDrive", Environment.GetEnvironmentVariable("OneDrive"));

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        TryAdd("iCloud Drive", System.IO.Path.Combine(profile, "iCloudDrive"));
        TryAdd("Box", System.IO.Path.Combine(profile, "Box"));

        // Dropbox, konumunu info.json'a yazar.
        foreach (var root in new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData })
        {
            var info = System.IO.Path.Combine(Environment.GetFolderPath(root), "Dropbox", "info.json");
            if (!File.Exists(info)) continue;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(info));
                foreach (var account in doc.RootElement.EnumerateObject())
                    if (account.Value.TryGetProperty("path", out var p) && p.GetString() is { } dropboxPath)
                        TryAdd(account.Name == "business" ? "Dropbox (iş)" : "Dropbox", dropboxPath);
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string? LoadIndirect(string resource)
    {
        if (!resource.StartsWith('@')) return resource;
        var buffer = new StringBuilder(260);
        return SHLoadIndirectString(resource, buffer, buffer.Capacity, IntPtr.Zero) == 0 ? buffer.ToString() : null;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);
}

/// <summary>Bulut dosyalarının yer tutucu durumu (dosya öznitelikleri).</summary>
public static class CloudState
{
    private const int RecallOnDataAccess = 0x00400000; // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS
    private const int RecallOnOpen = 0x00040000;       // FILE_ATTRIBUTE_RECALL_ON_OPEN
    private const int Pinned = 0x00080000;             // FILE_ATTRIBUTE_PINNED ("her zaman bu cihazda tut")

    /// <summary>Dosya yalnızca çevrimiçi mi: açmak onu indirir.</summary>
    public static bool IsOnlineOnly(FileAttributes attributes) =>
        ((int)attributes & (RecallOnDataAccess | RecallOnOpen)) != 0 || attributes.HasFlag(FileAttributes.Offline);

    public static bool IsPinned(FileAttributes attributes) => ((int)attributes & Pinned) != 0;
}

/// <summary>Ağ paylaşımına (SMB) kimlik bilgisiyle bağlanır; sürücü harfi atamaz.</summary>
public static class NetworkShares
{
    /// <summary>Başarılıysa 0; aksi halde Win32 hata kodu.</summary>
    public static int Connect(string shareRoot, string? user, string? password)
    {
        var resource = new NETRESOURCE
        {
            dwType = 1, // RESOURCETYPE_DISK
            lpRemoteName = shareRoot,
        };
        int result = WNetAddConnection2(ref resource, string.IsNullOrEmpty(password) ? null : password,
            string.IsNullOrEmpty(user) ? null : user, 0);
        return result == 85 /* ERROR_ALREADY_ASSIGNED */ ? 0 : result;
    }

    public static string Describe(int error) => error switch
    {
        5 => "Erişim reddedildi.",
        53 or 67 => "Paylaşım bulunamadı. Sunucu adını ve paylaşım adını kontrol edin.",
        86 or 1326 => "Kullanıcı adı ya da parola yanlış.",
        1219 => "Bu sunucuya başka bir kullanıcıyla zaten bağlısınız. Windows aynı sunucuya iki farklı kullanıcıyla bağlanmaya izin vermez.",
        1203 or 1222 => "Ağa ulaşılamıyor.",
        _ => new System.ComponentModel.Win32Exception(error).Message,
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string? lpLocalName;
        public string? lpRemoteName;
        public string? lpComment;
        public string? lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NETRESOURCE netResource, string? password, string? username, int flags);
}
