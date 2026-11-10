using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace ZeroFileCommander.Services;

/// <summary>Bir dosyanın Git durumu (önem sırasına göre).</summary>
public enum GitState
{
    None = 0,
    Ignored,
    Untracked,
    Added,
    Deleted,
    Modified,
    Conflict,
}

/// <summary>Bir deponun anlık durumu: dal, ileri/geri ve değişen yollar (depo köküne göre, "/" ile).</summary>
public sealed class GitInfo
{
    public required string Root { get; init; }
    public string Branch { get; set; } = string.Empty;
    public int Ahead { get; set; }
    public int Behind { get; set; }
    public bool HasUpstream { get; set; }

    /// <summary>false: git.exe bulunamadı, yalnızca dal adı .git/HEAD'den okundu.</summary>
    public bool HasStatus { get; set; }

    public Dictionary<string, GitState> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, GitState> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> UntrackedFolders { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> IgnoredFolders { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int ChangeCount { get; set; }

    /// <summary>Tam yol için durum. Klasörlerde altındaki en önemli değişiklik döner.</summary>
    public GitState StateOf(string fullPath, bool isDirectory)
    {
        string relative = Path.GetRelativePath(Root, fullPath).Replace('\\', '/');
        if (relative == "." || relative.StartsWith("../", StringComparison.Ordinal)) return GitState.None;
        if (relative.Equals(".git", StringComparison.OrdinalIgnoreCase)) return GitState.Ignored;

        if (isDirectory)
        {
            if (UntrackedFolders.Contains(relative)) return GitState.Untracked;
            if (IgnoredFolders.Contains(relative)) return GitState.Ignored;
            if (Folders.TryGetValue(relative, out var folderState)) return folderState;
        }
        else if (Files.TryGetValue(relative, out var fileState))
        {
            return fileState;
        }

        // İzlenmeyen/yok sayılan bir klasörün içindeysek her şey o durumdadır.
        int slash = relative.LastIndexOf('/');
        while (slash > 0)
        {
            relative = relative[..slash];
            if (UntrackedFolders.Contains(relative)) return GitState.Untracked;
            if (IgnoredFolders.Contains(relative)) return GitState.Ignored;
            slash = relative.LastIndexOf('/');
        }
        return GitState.None;
    }

    public string Summary
    {
        get
        {
            var parts = new List<string> { Branch };
            if (Ahead > 0) parts.Add($"↑{Ahead}");
            if (Behind > 0) parts.Add($"↓{Behind}");
            return string.Join(" ", parts);
        }
    }
}

/// <summary>
/// git.exe ile <c>status --porcelain</c> çalıştırır (kısa zaman aşımıyla, önbellekli).
/// Git kurulu değilse yalnızca dal adını .git/HEAD'den okur.
/// </summary>
public static class GitService
{
    private static readonly Lazy<string?> GitPath = new(FindGit);
    private static readonly ConcurrentDictionary<string, (DateTime Time, Task<GitInfo?> Task)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(2);

    public static bool IsGitInstalled => GitPath.Value is not null;

    /// <summary>Klasörü içeren deponun kökü (.git klasörü ya da çalışma ağacı dosyası olan ilk üst klasör).</summary>
    public static string? FindRoot(string folder)
    {
        try
        {
            var directory = new DirectoryInfo(folder);
            int depth = 0;
            while (directory is not null && depth++ < 64)
            {
                var git = Path.Combine(directory.FullName, ".git");
                if (Directory.Exists(git) || File.Exists(git)) return directory.FullName;
                directory = directory.Parent;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
        }
        return null;
    }

    /// <summary>Aynı depo için kısa süre içinde gelen istekler (iki panel, yenileme) tek çağrıyı paylaşır.</summary>
    public static Task<GitInfo?> GetAsync(string root, bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && Cache.TryGetValue(root, out var cached) && now - cached.Time < CacheLifetime)
            return cached.Task;
        var task = Task.Run(() => Read(root));
        Cache[root] = (now, task);
        return task;
    }

    public static void Invalidate(string root) => Cache.TryRemove(root, out _);

    private static GitInfo? Read(string root)
    {
        var info = new GitInfo { Root = root, Branch = ReadHeadBranch(root) ?? "?" };
        var git = GitPath.Value;
        if (git is null) return info;

        var output = Run(git, root, "--no-optional-locks", "-c", "core.quotepath=off", "status", "--porcelain=v1", "-b", "-z",
            "--no-renames", "--untracked-files=normal", "--ignored=traditional", "--ignore-submodules=dirty");
        if (output is null) return info;
        info.HasStatus = true;
        Parse(output, info);
        return info;
    }

    private static void Parse(string output, GitInfo info)
    {
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (record.StartsWith("## ", StringComparison.Ordinal))
            {
                ParseBranch(record[3..], info);
                continue;
            }
            if (record.Length < 4) continue;
            char x = record[0], y = record[1];
            string path = record[3..];
            bool folder = path.EndsWith('/');
            path = path.TrimEnd('/');

            if (x == '!' && y == '!')
            {
                if (folder) info.IgnoredFolders.Add(path);
                else info.Files[path] = GitState.Ignored;
                continue;
            }

            var state = Classify(x, y);
            info.ChangeCount++;
            if (folder && state == GitState.Untracked) info.UntrackedFolders.Add(path);
            else info.Files[path] = state;

            // Üst klasörlere en önemli durumu yay.
            int slash = path.LastIndexOf('/');
            while (slash > 0)
            {
                path = path[..slash];
                if (!info.Folders.TryGetValue(path, out var existing) || existing < state)
                    info.Folders[path] = state;
                slash = path.LastIndexOf('/');
            }
        }
    }

    private static GitState Classify(char x, char y)
    {
        if (x == '?' && y == '?') return GitState.Untracked;
        if (x == 'U' || y == 'U' || (x == 'A' && y == 'A') || (x == 'D' && y == 'D')) return GitState.Conflict;
        if (x == 'D' || y == 'D') return GitState.Deleted;
        if (x == 'A') return y == 'M' ? GitState.Modified : GitState.Added;
        return GitState.Modified;
    }

    /// <summary>"main...origin/main [ahead 1, behind 2]", "No commits yet on main", "HEAD (no branch)".</summary>
    private static void ParseBranch(string header, GitInfo info)
    {
        const string noCommits = "No commits yet on ";
        if (header.StartsWith(noCommits, StringComparison.Ordinal))
        {
            info.Branch = header[noCommits.Length..].Trim();
            return;
        }
        if (header.StartsWith("HEAD (no branch)", StringComparison.Ordinal))
        {
            info.Branch = "ayrık HEAD";
            return;
        }
        string branch = header;
        int bracket = header.IndexOf(" [", StringComparison.Ordinal);
        if (bracket > 0)
        {
            var counts = header[(bracket + 2)..].TrimEnd(']');
            foreach (var part in counts.Split(", "))
            {
                if (part.StartsWith("ahead ", StringComparison.Ordinal) && int.TryParse(part[6..], out int ahead)) info.Ahead = ahead;
                else if (part.StartsWith("behind ", StringComparison.Ordinal) && int.TryParse(part[7..], out int behind)) info.Behind = behind;
            }
            branch = header[..bracket];
        }
        int dots = branch.IndexOf("...", StringComparison.Ordinal);
        if (dots > 0)
        {
            info.HasUpstream = true;
            branch = branch[..dots];
        }
        info.Branch = branch.Trim();
    }

    /// <summary>git.exe yoksa dal adını doğrudan HEAD dosyasından oku.</summary>
    private static string? ReadHeadBranch(string root)
    {
        try
        {
            var gitPath = Path.Combine(root, ".git");
            string gitDir = gitPath;
            if (File.Exists(gitPath))
            {
                // Çalışma ağacı / alt modül: "gitdir: ../.git/worktrees/x"
                var line = File.ReadAllText(gitPath).Trim();
                if (!line.StartsWith("gitdir:", StringComparison.Ordinal)) return null;
                gitDir = Path.GetFullPath(Path.Combine(root, line[7..].Trim()));
            }
            var head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
            const string prefix = "ref: refs/heads/";
            if (head.StartsWith(prefix, StringComparison.Ordinal)) return head[prefix.Length..];
            return head.Length >= 7 ? head[..7] : head;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? Run(string git, string workingDirectory, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(git)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(false),
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            // Kimlik sorusu veya düzenleyici açılmasın.
            start.Environment["GIT_TERMINAL_PROMPT"] = "0";
            start.Environment["GIT_OPTIONAL_LOCKS"] = "0";

            using var process = Process.Start(start);
            if (process is null) return null;
            var stdout = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return null;
            }
            return process.ExitCode == 0 && stdout.Wait(1000) ? stdout.Result : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static string? FindGit()
    {
        var candidates = new List<string>();
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                candidates.Add(Path.Combine(folder.Trim().Trim('"'), "git.exe"));
            }
            catch (ArgumentException)
            {
            }
        }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }
}
