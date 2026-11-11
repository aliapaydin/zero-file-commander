using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ZeroFileCommander.Models;

namespace ZeroFileCommander.Services;

public enum RenameCase
{
    Unchanged,
    Lower,
    Upper,
    Title,
}

public sealed record RenameOptions(
    string NameMask,
    string ExtensionMask,
    string Find,
    string Replace,
    bool UseRegex,
    RenameCase Case,
    int CounterStart,
    int CounterStep,
    int CounterDigits);

/// <summary>Önizleme satırı.</summary>
public sealed class RenameRow
{
    public string FullPath { get; set; } = string.Empty;
    public string OldName { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
    public string Problem { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public bool HasProblem => Problem.Length > 0;
    public bool IsChanged => !HasProblem && !string.Equals(OldName, NewName, StringComparison.Ordinal);

    /// <summary>Listede gösterilen durum metni.</summary>
    public string StatusText => HasProblem ? Problem : IsChanged ? "→" : Loc.T("değişmez");
}

/// <summary>
/// Total Commander'ın "Çoklu Yeniden Adlandırma" aracına benzer şablonlu yeniden adlandırma.
/// Şablon belirteçleri: [N] ad, [N2-5] adın 2–5. karakterleri, [E] uzantı, [C] sayaç,
/// [D] tarih (yyyy-MM-dd), [T] saat (HH-mm-ss), [P] üst klasör adı.
/// </summary>
public static class BatchRename
{
    private static readonly Regex Token = new(@"\[(N(\d+)?(-(\d+))?|E|C|D|T|P)\]", RegexOptions.IgnoreCase);

    public static List<RenameRow> Preview(IReadOnlyList<FileItem> items, RenameOptions options)
    {
        var rows = new List<RenameRow>();
        int counter = options.CounterStart;
        Regex? find = null;
        string? findError = null;
        if (options.UseRegex && options.Find.Length > 0)
        {
            try { find = new Regex(options.Find, RegexOptions.IgnoreCase); }
            catch (ArgumentException ex) { findError = Loc.T("Geçersiz regex: ") + ex.Message; }
        }

        foreach (var item in items)
        {
            var row = new RenameRow { FullPath = item.FullPath, OldName = item.Name, IsDirectory = item.IsDirectory };
            string stem = item.IsDirectory ? item.Name : Path.GetFileNameWithoutExtension(item.Name);
            string ext = item.IsDirectory ? string.Empty : Path.GetExtension(item.Name).TrimStart('.');
            string parent = Path.GetFileName(Path.GetDirectoryName(item.FullPath.TrimEnd('\\')) ?? string.Empty);
            var date = item.Modified ?? DateTime.Now;

            string Expand(string mask) => Token.Replace(mask, m =>
            {
                var token = m.Groups[1].Value.ToUpperInvariant();
                if (token.StartsWith('N'))
                {
                    if (!m.Groups[2].Success) return stem;
                    int from = Math.Max(1, int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
                    int to = m.Groups[4].Success ? int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : from;
                    if (from > stem.Length) return string.Empty;
                    to = Math.Min(Math.Max(to, from), stem.Length);
                    return stem.Substring(from - 1, to - from + 1);
                }
                return token switch
                {
                    "E" => ext,
                    "C" => counter.ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(1, options.CounterDigits), '0'),
                    "D" => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    "T" => date.ToString("HH-mm-ss", CultureInfo.InvariantCulture),
                    "P" => parent,
                    _ => m.Value,
                };
            });

            string newStem = Expand(options.NameMask.Length == 0 ? "[N]" : options.NameMask);
            string newExt = item.IsDirectory ? string.Empty : Expand(options.ExtensionMask.Length == 0 ? "[E]" : options.ExtensionMask);
            string name = newExt.Length > 0 ? $"{newStem}.{newExt}" : newStem;

            if (findError is not null)
            {
                row.Problem = findError;
            }
            else if (options.Find.Length > 0)
            {
                name = find is not null
                    ? find.Replace(name, options.Replace)
                    : name.Replace(options.Find, options.Replace, StringComparison.CurrentCultureIgnoreCase);
            }

            name = options.Case switch
            {
                RenameCase.Lower => name.ToLower(CultureInfo.CurrentCulture),
                RenameCase.Upper => name.ToUpper(CultureInfo.CurrentCulture),
                RenameCase.Title => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLower(CultureInfo.CurrentCulture)),
                _ => name,
            };
            row.NewName = name.Trim();
            rows.Add(row);
            counter += options.CounterStep;
        }

        Validate(rows);
        return rows;
    }

    private static void Validate(List<RenameRow> rows)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var batchSources = new HashSet<string>(rows.Select(r => r.FullPath), StringComparer.OrdinalIgnoreCase);
        var seen = new Dictionary<string, RenameRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (row.HasProblem) continue;
            if (row.NewName.Length == 0) { row.Problem = Loc.T("Boş ad"); continue; }
            if (row.NewName.IndexOfAny(invalid) >= 0) { row.Problem = Loc.T("Geçersiz karakter"); continue; }
            if (row.NewName.EndsWith('.')) { row.Problem = Loc.T("Nokta ile bitemez"); continue; }

            var folder = Path.GetDirectoryName(row.FullPath.TrimEnd('\\'))!;
            var target = Path.Combine(folder, row.NewName);
            if (seen.TryGetValue(target, out var other))
            {
                row.Problem = Loc.T("Aynı ad iki kez");
                other.Problem = Loc.T("Aynı ad iki kez");
                continue;
            }
            seen[target] = row;

            bool exists = File.Exists(target) || Directory.Exists(target);
            bool self = string.Equals(target, row.FullPath, StringComparison.OrdinalIgnoreCase);
            if (exists && !self && !batchSources.Contains(target)) row.Problem = Loc.T("Bu adda dosya var");
        }
    }

    /// <summary>
    /// Yeniden adlandırır. Zincirleme çakışmalar (a→b, b→a) için önce geçici adlara taşır.
    /// Geri almak için (yeni yol, eski yol) çiftlerini döner.
    /// </summary>
    public static List<(string NewPath, string OldPath)> Apply(IReadOnlyList<RenameRow> rows)
    {
        var changes = rows.Where(r => r.IsChanged).ToList();
        var staged = new List<(RenameRow Row, string Temp)>();
        var done = new List<(string, string)>();
        try
        {
            foreach (var row in changes)
            {
                var folder = Path.GetDirectoryName(row.FullPath.TrimEnd('\\'))!;
                var temp = Path.Combine(folder, $".zfc-rename-{Guid.NewGuid():N}");
                Move(row.FullPath, temp, row.IsDirectory);
                staged.Add((row, temp));
            }
            foreach (var (row, temp) in staged)
            {
                var folder = Path.GetDirectoryName(row.FullPath.TrimEnd('\\'))!;
                var target = Path.Combine(folder, row.NewName);
                Move(temp, target, row.IsDirectory);
                done.Add((target, row.FullPath));
            }
            return done;
        }
        catch
        {
            // Yarım kalırsa geçici adlardakileri eski adlarına döndürmeyi dene.
            foreach (var (row, temp) in staged)
            {
                try
                {
                    if (File.Exists(temp) || Directory.Exists(temp)) Move(temp, row.FullPath, row.IsDirectory);
                }
                catch
                {
                    // elden geldiğince
                }
            }
            throw;
        }
    }

    public static void Undo(IReadOnlyList<(string NewPath, string OldPath)> batch)
    {
        foreach (var (newPath, oldPath) in batch.Reverse())
        {
            bool isDirectory = Directory.Exists(newPath);
            if (!isDirectory && !File.Exists(newPath)) continue;
            Move(newPath, oldPath, isDirectory);
        }
    }

    private static void Move(string from, string to, bool isDirectory)
    {
        if (isDirectory) Directory.Move(from, to);
        else File.Move(from, to);
    }

    /// <summary>Kısa açıklama (önizleme altı).</summary>
    public static string Summary(IReadOnlyList<RenameRow> rows)
    {
        int changed = rows.Count(r => r.IsChanged);
        int problems = rows.Count(r => r.HasProblem);
        var text = new StringBuilder(Loc.F("{0} öğe · {1} değişecek", rows.Count, changed));
        if (problems > 0) text.Append(Loc.F(" · {0} sorun", problems));
        return text.ToString();
    }
}
