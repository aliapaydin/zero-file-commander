using System.Globalization;
using System.IO.Compression;
using System.Text;
using ZeroFileCommander.Models;

namespace ZeroFileCommander.Services;

public enum OverwriteMode
{
    Overwrite,
    Skip,
}

/// <summary>
/// ZIP arşivlerini klasör gibi listeler; çıkarma, ekleme, silme ve sıkıştırma yapar.
/// Arşiv içi yollar her zaman "/" ile ayrılır.
/// </summary>
public static class ArchiveService
{
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "ZeroFileCommander", "archive");

    static ArchiveService()
    {
        // Eski ZIP'lerde Türkçe adlar OEM kod sayfasıyla (857) yazılmış olabilir.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static Encoding LegacyNameEncoding
    {
        get
        {
            try
            {
                return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            }
            catch
            {
                return Encoding.UTF8;
            }
        }
    }

    public static bool IsArchive(string path) =>
        string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>"C:\a\b.zip\klasor\alt" gibi bir yolu arşiv dosyası + iç yol olarak ayırır.</summary>
    public static bool TrySplit(string path, out string archive, out string inner)
    {
        archive = string.Empty;
        inner = string.Empty;
        if (string.IsNullOrEmpty(path)) return false;

        var normalized = path.Replace('/', '\\').TrimEnd('\\');
        if (File.Exists(normalized) && IsArchive(normalized))
        {
            archive = normalized;
            return true;
        }

        int index = 0;
        while ((index = normalized.IndexOf('\\', index + 1)) > 0)
        {
            var candidate = normalized[..index];
            if (IsArchive(candidate) && File.Exists(candidate))
            {
                archive = candidate;
                inner = normalized[(index + 1)..].Replace('\\', '/').Trim('/');
                if (inner.Length > 0) inner += "/";
                return true;
            }
        }
        return false;
    }

    public static string DisplayPath(string archive, string inner) =>
        inner.Length == 0 ? archive : archive + "\\" + inner.TrimEnd('/').Replace('/', '\\');

    private static ZipArchive OpenRead(string archive) =>
        new(File.OpenRead(archive), ZipArchiveMode.Read, leaveOpen: false, LegacyNameEncoding);

    /// <summary>Arşivdeki bir düzeyin öğeleri (önce klasörler).</summary>
    public static List<FileItem> List(string archive, string inner)
    {
        var folders = new Dictionary<string, FileItem>(StringComparer.OrdinalIgnoreCase);
        var files = new List<FileItem>();
        using var zip = OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith(inner, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = name[inner.Length..];
            if (rest.Length == 0) continue;

            int slash = rest.IndexOf('/');
            if (slash >= 0)
            {
                var folder = rest[..slash];
                if (folder.Length == 0 || folders.ContainsKey(folder)) continue;
                folders[folder] = new FileItem
                {
                    Name = folder,
                    FullPath = DisplayPath(archive, inner + folder + "/"),
                    IsDirectory = true,
                    ArchivePath = archive,
                    EntryName = inner + folder + "/",
                    Modified = entry.LastWriteTime.LocalDateTime,
                };
            }
            else
            {
                files.Add(new FileItem
                {
                    Name = rest,
                    FullPath = DisplayPath(archive, inner) + "\\" + rest,
                    Size = entry.Length,
                    Modified = entry.LastWriteTime.LocalDateTime,
                    ArchivePath = archive,
                    EntryName = name,
                });
            }
        }

        var parentPath = inner.Length == 0
            ? Path.GetDirectoryName(archive) ?? string.Empty
            : DisplayPath(archive, ParentInner(inner));
        var list = new List<FileItem> { new() { Name = "..", FullPath = parentPath, IsDirectory = true, IsParent = true } };
        list.AddRange(folders.Values);
        list.AddRange(files);
        return list;
    }

    private static string ParentInner(string inner)
    {
        var trimmed = inner.TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        return slash < 0 ? string.Empty : trimmed[..(slash + 1)];
    }

    /// <summary>Seçili arşiv öğelerini (dosya ya da klasör) hedef klasöre çıkarır.</summary>
    public static Task<int> ExtractAsync(IEnumerable<FileItem> items, string targetFolder, OverwriteMode mode, CancellationToken token = default)
    {
        var selected = items.Where(i => i.IsInArchive).ToList();
        return Task.Run(() =>
        {
            int count = 0;
            foreach (var group in selected.GroupBy(i => i.ArchivePath!, StringComparer.OrdinalIgnoreCase))
            {
                using var zip = OpenRead(group.Key);
                foreach (var item in group)
                {
                    var entryName = item.EntryName!;
                    // Klasörün üst düzeyi, hedefte bu öğenin adıyla başlasın.
                    var baseInner = item.IsDirectory ? ParentInner(entryName) : entryName[..(entryName.LastIndexOf('/') + 1)];
                    foreach (var entry in zip.Entries)
                    {
                        token.ThrowIfCancellationRequested();
                        var name = entry.FullName.Replace('\\', '/');
                        bool match = item.IsDirectory
                            ? name.StartsWith(entryName, StringComparison.OrdinalIgnoreCase)
                            : string.Equals(name, entryName, StringComparison.OrdinalIgnoreCase);
                        if (!match) continue;
                        if (ExtractEntry(entry, name[baseInner.Length..], targetFolder, mode)) count++;
                    }
                }
            }
            return count;
        }, token);
    }

    /// <summary>Bir arşivin tamamını hedef klasöre çıkarır.</summary>
    public static Task<int> ExtractAllAsync(string archive, string targetFolder, OverwriteMode mode, CancellationToken token = default) =>
        Task.Run(() =>
        {
            int count = 0;
            using var zip = OpenRead(archive);
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (ExtractEntry(entry, entry.FullName.Replace('\\', '/'), targetFolder, mode)) count++;
            }
            return count;
        }, token);

    private static bool ExtractEntry(ZipArchiveEntry entry, string relative, string targetFolder, OverwriteMode mode)
    {
        if (relative.Length == 0) return false;
        var root = Path.GetFullPath(targetFolder);
        var destination = Path.GetFullPath(Path.Combine(root, relative.Replace('/', '\\')));
        // "Zip slip" koruması: hedef klasörün dışına yazma.
        if (!destination.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return false;

        if (relative.EndsWith('/'))
        {
            Directory.CreateDirectory(destination);
            return false;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination) && mode == OverwriteMode.Skip) return false;
        entry.ExtractToFile(destination, overwrite: true);
        return true;
    }

    /// <summary>Önizleme/açma için tek bir dosyayı geçici klasöre çıkarır.</summary>
    public static Task<string> ExtractToTempAsync(FileItem item) => Task.Run(() =>
    {
        var folder = Path.Combine(TempRoot, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        using var zip = OpenRead(item.ArchivePath!);
        var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), item.EntryName, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException("Arşivde bulunamadı", item.EntryName);
        var path = Path.Combine(folder, item.Name);
        entry.ExtractToFile(path, overwrite: true);
        return path;
    });

    /// <summary>Dosya/klasörleri arşivin içine (verilen iç klasöre) ekler.</summary>
    public static Task<int> AddAsync(string archive, string inner, IEnumerable<string> paths, CancellationToken token = default)
    {
        var sources = paths.ToList();
        return Task.Run(() =>
        {
            int count = 0;
            using var stream = new FileStream(archive, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: false, LegacyNameEncoding);
            foreach (var source in sources)
            {
                token.ThrowIfCancellationRequested();
                if (Directory.Exists(source))
                {
                    var baseName = Path.GetFileName(source.TrimEnd('\\'));
                    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                    {
                        token.ThrowIfCancellationRequested();
                        var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                        AddFile(zip, file, inner + baseName + "/" + relative);
                        count++;
                    }
                }
                else if (File.Exists(source))
                {
                    AddFile(zip, source, inner + Path.GetFileName(source));
                    count++;
                }
            }
            return count;
        }, token);
    }

    private static void AddFile(ZipArchive zip, string file, string entryName)
    {
        zip.GetEntry(entryName)?.Delete();
        zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
    }

    /// <summary>Arşivden öğeleri siler (klasörler içerikleriyle).</summary>
    public static Task<int> DeleteAsync(IEnumerable<FileItem> items)
    {
        var selected = items.Where(i => i.IsInArchive).ToList();
        return Task.Run(() =>
        {
            int count = 0;
            foreach (var group in selected.GroupBy(i => i.ArchivePath!, StringComparer.OrdinalIgnoreCase))
            {
                using var stream = new FileStream(group.Key, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: false, LegacyNameEncoding);
                foreach (var entry in zip.Entries.ToList())
                {
                    var name = entry.FullName.Replace('\\', '/');
                    if (group.Any(i => i.IsDirectory
                            ? name.StartsWith(i.EntryName!, StringComparison.OrdinalIgnoreCase)
                            : string.Equals(name, i.EntryName, StringComparison.OrdinalIgnoreCase)))
                    {
                        entry.Delete();
                        count++;
                    }
                }
            }
            return count;
        });
    }

    /// <summary>Dosya/klasörlerden yeni bir ZIP oluşturur.</summary>
    public static Task CreateAsync(string archive, IEnumerable<string> paths, CancellationToken token = default)
    {
        var sources = paths.ToList();
        return Task.Run(() =>
        {
            using var stream = new FileStream(archive, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);
            foreach (var source in sources)
            {
                token.ThrowIfCancellationRequested();
                if (Directory.Exists(source))
                {
                    var baseName = Path.GetFileName(source.TrimEnd('\\'));
                    zip.CreateEntry(baseName + "/");
                    foreach (var entryPath in Directory.EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories))
                    {
                        token.ThrowIfCancellationRequested();
                        var relative = baseName + "/" + Path.GetRelativePath(source, entryPath).Replace('\\', '/');
                        if (Directory.Exists(entryPath)) zip.CreateEntry(relative + "/");
                        else zip.CreateEntryFromFile(entryPath, relative, CompressionLevel.Optimal);
                    }
                }
                else if (File.Exists(source))
                {
                    zip.CreateEntryFromFile(source, Path.GetFileName(source), CompressionLevel.Optimal);
                }
            }
        }, token);
    }

    /// <summary>Hedefte zaten var olacak dosya sayısı (çakışma sorusu için).</summary>
    public static int CountConflicts(IEnumerable<FileItem> items, string targetFolder)
    {
        int conflicts = 0;
        foreach (var item in items)
        {
            var target = Path.Combine(targetFolder, item.Name);
            if (item.IsDirectory ? Directory.Exists(target) : File.Exists(target)) conflicts++;
        }
        return conflicts;
    }
}
