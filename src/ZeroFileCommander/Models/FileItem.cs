using System.ComponentModel;
using Microsoft.UI.Xaml.Media;

namespace ZeroFileCommander.Models;

/// <summary>Bir paneldeki tek satır: sürücü, klasör, dosya ya da "..".</summary>
public sealed class FileItem : INotifyPropertyChanged
{
    private FileKind? _kind;
    private ImageSource? _icon;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public bool IsParent { get; set; }
    public bool IsDrive { get; set; }
    public long? Size { get; set; }
    public DateTime? Modified { get; set; }

    /// <summary>Öğe bir ZIP arşivinin içindeyse arşiv dosyasının yolu.</summary>
    public string? ArchivePath { get; set; }

    /// <summary>Arşiv içindeki tam ad ("klasor/dosya.txt", klasörlerde "/" ile biter).</summary>
    public string? EntryName { get; set; }

    public bool IsInArchive => ArchivePath is not null;

    /// <summary>Gerçek bir dosya ya da klasör mü ("..", sürücü değil).</summary>
    public bool IsEntry => !IsParent && !IsDrive;

    public FileKind Kind => _kind ??= FileKinds.Of(this);

    public string Extension =>
        IsDirectory ? string.Empty : Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();

    /// <summary>Uzantı sütununda gösterilen metin.</summary>
    public string ExtensionText => IsDirectory ? string.Empty : Extension.ToLowerInvariant();

    public string SizeText =>
        IsParent ? string.Empty :
        IsDrive ? string.Empty :
        IsDirectory ? "<KLS>" :
        FormatSize(Size ?? 0);

    public string ModifiedText => Modified?.ToString("dd.MM.yyyy HH:mm") ?? string.Empty;

    // Segoe Fluent Icons (gerçek simge yüklenene kadar ya da ".." için)
    public string Glyph =>
        IsParent ? "" :
        IsDrive ? "" :
        FileKinds.Glyph(Kind);

    /// <summary>Windows'un bu dosya türü için kullandığı simge (gecikmeli yüklenir).</summary>
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value)) return;
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GlyphOpacity)));
        }
    }

    /// <summary>Gerçek simge varsa yazı tipi simgesi gizlenir.</summary>
    public double GlyphOpacity => _icon is null ? 1 : 0;

    /// <summary>Aynı dosyanın içeriği değişmemiş mi (yenilemede nesneyi korumak için).</summary>
    public bool SameAs(FileItem other) =>
        IsDirectory == other.IsDirectory && Size == other.Size && Modified == other.Modified && Name == other.Name;

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}
