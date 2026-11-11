using System.Diagnostics;
using Microsoft.Win32;

namespace ZeroFileCommander.Services;

/// <summary>Kurulu Visual Studio Code'u bulur (kullanıcı/sistem kurulumu, Insiders, PATH) ve dosya/klasör açar.</summary>
public static class VsCode
{
    private static string? _path;
    private static DateTime _checked;

    /// <summary>Code.exe yolu; kurulu değilse null. Sonuç bir dakika önbelleklenir (sonradan kurulursa fark edilsin).</summary>
    public static string? ExecutablePath
    {
        get
        {
            if (DateTime.UtcNow - _checked < TimeSpan.FromMinutes(1)) return _path;
            _path = Find();
            _checked = DateTime.UtcNow;
            return _path;
        }
    }

    public static bool IsInstalled => ExecutablePath is not null;

    /// <summary>Kod, metin, not defteri ve klasörler VS Code ile açılmaya uygun.</summary>
    public static bool Suits(Models.FileItem item) =>
        item.IsDirectory ||
        item.Kind == Models.FileKind.Text ||
        SyntaxHighlighter.IsSupported(item.Name) ||
        item.Extension is "IPYNB" or "CSV" or "TSV" or "LOG" or "SVG";

    public static bool Open(string path)
    {
        var exe = ExecutablePath;
        if (exe is null || path.Length == 0) return false;
        try
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false };
            start.ArgumentList.Add(path);
            Process.Start(start);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            CrashLog.Write(Loc.T("VS Code açılamadı"), ex);
            return false;
        }
    }

    private static string? Find()
    {
        var candidates = new List<string>();
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var root in new[] { Path.Combine(local, "Programs"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (string.IsNullOrEmpty(root)) continue;
            candidates.Add(Path.Combine(root, "Microsoft VS Code", "Code.exe"));
            candidates.Add(Path.Combine(root, "Microsoft VS Code Insiders", "Code - Insiders.exe"));
        }

        // Kaldırma kayıtları: özel klasöre kurulmuş olabilir.
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var uninstall = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var key = uninstall.OpenSubKey(name);
                    if (key?.GetValue("DisplayName") is not string display || !display.StartsWith("Microsoft Visual Studio Code", StringComparison.Ordinal)) continue;
                    if (key.GetValue("InstallLocation") is string location && location.Length > 0)
                    {
                        candidates.Add(Path.Combine(location, "Code.exe"));
                        candidates.Add(Path.Combine(location, "Code - Insiders.exe"));
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
            }
        }

        // PATH: ...\Microsoft VS Code\bin\code.cmd → ..\Code.exe
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var trimmed = folder.Trim().Trim('"');
                if (File.Exists(Path.Combine(trimmed, "code.cmd")) && Path.GetDirectoryName(trimmed.TrimEnd('\\')) is { } parent)
                    candidates.Add(Path.Combine(parent, "Code.exe"));
            }
            catch (ArgumentException)
            {
            }
        }
        return candidates.FirstOrDefault(File.Exists);
    }
}
