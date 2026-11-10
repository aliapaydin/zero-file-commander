namespace ZeroFileCommander.Models;

/// <summary>Bir paneldeki tek satır: sürücü, klasör, dosya ya da "..".</summary>
public sealed class FileItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public bool IsParent { get; set; }
    public bool IsDrive { get; set; }
    public long? Size { get; set; }
    public DateTime? Modified { get; set; }

    public string Extension =>
        IsDirectory ? string.Empty : Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();

    public string SizeText =>
        IsParent ? string.Empty :
        IsDrive ? string.Empty :
        IsDirectory ? "<KLS>" :
        FormatSize(Size ?? 0);

    public string ModifiedText => Modified?.ToString("dd.MM.yyyy HH:mm") ?? string.Empty;

    // Segoe Fluent Icons
    public string Glyph =>
        IsParent ? "" :   // Up
        IsDrive ? "" :    // HardDrive
        IsDirectory ? "" : // Folder
        "";                // Document

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
