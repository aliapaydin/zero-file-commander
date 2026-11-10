using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ZeroFileCommander.Services;

/// <summary>"Yeni" menüsündeki bir dosya türü.</summary>
public sealed record ShellNewEntry(
    string Extension,
    string DisplayName,
    string BaseName,
    string? TemplatePath,
    byte[]? Data);

/// <summary>
/// Dosya Gezgini'nin "Yeni" menüsündeki türleri kayıt defterinden (ShellNew) okur;
/// Word, Excel, PowerPoint gibi kurulu uygulamaların şablonlarını kullanır.
/// </summary>
public static class ShellNew
{
    private static IReadOnlyList<ShellNewEntry>? _cache;

    public static IReadOnlyList<ShellNewEntry> Entries => _cache ??= Load();

    /// <summary>Verilen klasörde, çakışmayan adla yeni dosya oluşturur ve tam yolunu döner.</summary>
    public static string Create(ShellNewEntry entry, string folder, string name)
    {
        var path = UniquePath(Path.Combine(folder, name));
        if (entry.TemplatePath is not null)
            File.Copy(entry.TemplatePath, path, overwrite: false);
        else
            File.WriteAllBytes(path, entry.Data ?? []);
        return path;
    }

    public static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    private static IReadOnlyList<ShellNewEntry> Load()
    {
        var extensions = new List<string>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Discardable\PostSetup\ShellNew");
            if (key?.GetValue("Classes") is string[] classes)
                extensions.AddRange(classes.Where(c => c.StartsWith('.')));
        }
        catch
        {
            // önbellek yoksa bilinen türlere bak
        }
        extensions.AddRange([".txt", ".rtf", ".docx", ".xlsx", ".pptx", ".bmp", ".zip"]);

        var result = new List<ShellNewEntry>();
        foreach (var ext in extensions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Probe(ext) is { } entry) result.Add(entry);
            }
            catch
            {
                // bozuk kayıt; atla
            }
        }

        if (!result.Any(e => e.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)))
            result.Add(new ShellNewEntry(".txt", "Metin Belgesi", "Yeni Metin Belgesi", null, null));
        result.Add(new ShellNewEntry(".md", "Markdown belgesi", "Yeni Markdown Belgesi", null, Encoding.UTF8.GetBytes("# Başlık\n\n")));

        // Önce Office ve metin, sonra diğerleri.
        static int Rank(string ext) => ext.ToLowerInvariant() switch
        {
            ".docx" => 0, ".xlsx" => 1, ".pptx" => 2, ".txt" => 3, ".md" => 4, ".rtf" => 5,
            _ => 10,
        };
        return result.OrderBy(e => Rank(e.Extension)).ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static ShellNewEntry? Probe(string ext)
    {
        using var extKey = Registry.ClassesRoot.OpenSubKey(ext);
        if (extKey is null) return null;
        var progId = extKey.GetValue(null) as string;

        RegistryKey? shellNew = extKey.OpenSubKey("ShellNew");
        if (shellNew is null && !string.IsNullOrEmpty(progId)) shellNew = extKey.OpenSubKey(progId + @"\ShellNew");
        if (shellNew is null) return null;

        using (shellNew)
        {
            var fileName = shellNew.GetValue("FileName") as string;
            var rawData = shellNew.GetValue("Data");
            bool nullFile = shellNew.GetValue("NullFile") is not null;
            // Sihirbaz başlatan türler (kısayol, kişi…) desteklenmiyor.
            if (shellNew.GetValue("Command") is not null && fileName is null && rawData is null && !nullFile) return null;

            string? template = fileName is null ? null : ResolveTemplate(fileName);
            byte[]? data = rawData switch
            {
                byte[] bytes => bytes,
                string text => Encoding.UTF8.GetBytes(text),
                _ => null,
            };
            if (template is null && data is null && !nullFile)
            {
                // Şablon bulunamadı; Word boş .docx'i açabiliyor, diğerleri bozuk sayılır.
                if (!ext.Equals(".docx", StringComparison.OrdinalIgnoreCase)) return null;
            }

            string display = ext.TrimStart('.').ToUpperInvariant() + " dosyası";
            if (!string.IsNullOrEmpty(progId))
            {
                using var progKey = Registry.ClassesRoot.OpenSubKey(progId);
                var friendly = ResolveIndirect(progKey?.GetValue("FriendlyTypeName") as string) ?? progKey?.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(friendly)) display = friendly;
            }

            var itemName = ResolveIndirect(shellNew.GetValue("ItemName") as string);
            string baseName = !string.IsNullOrWhiteSpace(itemName) ? itemName : $"Yeni {display}";
            return new ShellNewEntry(ext, display, baseName, template, data);
        }
    }

    private static string? ResolveTemplate(string fileName)
    {
        fileName = Environment.ExpandEnvironmentVariables(fileName);
        if (Path.IsPathRooted(fileName)) return File.Exists(fileName) ? fileName : null;

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string[] candidates =
        [
            Path.Combine(windows, "ShellNew", fileName),
            Path.Combine(programFiles, @"Microsoft Office\root\vfs\Windows\ShellNew", fileName),
            Path.Combine(programFilesX86, @"Microsoft Office\root\vfs\Windows\ShellNew", fileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Templates), fileName),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>"@C:\...dll,-123" biçimindeki yerelleştirilmiş metinleri çözer.</summary>
    private static string? ResolveIndirect(string? value)
    {
        if (string.IsNullOrEmpty(value) || value[0] != '@') return value;
        var buffer = new StringBuilder(512);
        return SHLoadIndirectString(value, buffer, buffer.Capacity, IntPtr.Zero) == 0 ? buffer.ToString() : null;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string source, StringBuilder output, int capacity, IntPtr reserved);
}
