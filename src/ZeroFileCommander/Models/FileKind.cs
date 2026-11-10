namespace ZeroFileCommander.Models;

public enum FileKind
{
    Other,
    Folder,
    Image,
    Audio,
    Video,
    Pdf,
    Text,
    Archive,
    Executable,
}

public static class FileKinds
{
    private static readonly Dictionary<string, FileKind> ByExtension = Build();

    private static Dictionary<string, FileKind> Build()
    {
        var map = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase);
        void Add(FileKind kind, params string[] exts)
        {
            foreach (var e in exts) map[e] = kind;
        }

        Add(FileKind.Image, "png", "jpg", "jpeg", "gif", "bmp", "webp", "tif", "tiff", "ico", "heic", "heif", "avif", "jxr");
        Add(FileKind.Audio, "mp3", "wav", "flac", "m4a", "aac", "wma", "ogg", "opus", "aiff", "alac");
        Add(FileKind.Video, "mp4", "mkv", "mov", "avi", "wmv", "webm", "m4v", "mpg", "mpeg", "3gp", "ts");
        Add(FileKind.Pdf, "pdf");
        Add(FileKind.Text, "txt", "md", "log", "csv", "tsv", "json", "xml", "yaml", "yml", "ini", "cfg", "conf", "toml",
            "cs", "xaml", "csproj", "sln", "js", "ts", "tsx", "jsx", "css", "scss", "html", "htm", "py", "ipynb", "sql",
            "ps1", "psm1", "bat", "cmd", "sh", "java", "kt", "swift", "go", "rs", "c", "cpp", "h", "hpp", "php", "rb",
            "r", "dax", "m", "gitignore", "editorconfig", "env");
        Add(FileKind.Archive, "zip", "7z", "rar", "tar", "gz", "tgz", "bz2", "xz", "cab", "iso");
        Add(FileKind.Executable, "exe", "msi", "msix", "appx", "bat", "cmd", "ps1");
        // Betikler hem metin hem çalıştırılabilir; önizleme için metin olarak sayılır.
        map["bat"] = FileKind.Text;
        map["cmd"] = FileKind.Text;
        map["ps1"] = FileKind.Text;
        return map;
    }

    public static FileKind Of(FileItem item)
    {
        if (item.IsDirectory) return FileKind.Folder;
        var ext = item.Extension;
        if (ext.Length == 0)
        {
            // ".gitignore" gibi uzantısız ama metin olan dosyalar
            ext = item.Name.TrimStart('.');
        }
        return ByExtension.TryGetValue(ext, out var kind) ? kind : FileKind.Other;
    }

    public static bool IsRunnable(FileItem item) =>
        !item.IsDirectory && item.Extension is "EXE" or "MSI" or "BAT" or "CMD";

    /// <summary>Üzerine gelince küçük resim kartı gösterilecek türler.</summary>
    public static bool SupportsHover(FileKind kind) =>
        kind is FileKind.Image or FileKind.Video or FileKind.Pdf;

    public static string Label(FileKind kind) => kind switch
    {
        FileKind.Folder => "Klasör",
        FileKind.Image => "Görsel",
        FileKind.Audio => "Ses",
        FileKind.Video => "Video",
        FileKind.Pdf => "PDF",
        FileKind.Text => "Metin / kod",
        FileKind.Archive => "Arşiv",
        FileKind.Executable => "Uygulama",
        _ => "Dosya",
    };

    public static string Glyph(FileKind kind) => kind switch
    {
        FileKind.Folder => "",
        FileKind.Image => "",
        FileKind.Audio => "",
        FileKind.Video => "",
        FileKind.Pdf => "",
        FileKind.Text => "",
        FileKind.Archive => "",
        FileKind.Executable => "",
        _ => "",
    };
}
