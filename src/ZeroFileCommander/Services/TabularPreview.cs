using System.Globalization;
using System.Text;
using Parquet;
using Parquet.Schema;

namespace ZeroFileCommander.Services;

/// <summary>Önizleme panelinde tablo olarak gösterilecek veri (ilk satırlar).</summary>
public sealed class TableData
{
    public List<string> Columns { get; } = new();
    public List<string> ColumnTypes { get; } = new();
    public List<string[]> Rows { get; } = new();

    /// <summary>Dosyadaki toplam satır; bilinmiyorsa null.</summary>
    public long? TotalRows { get; set; }

    /// <summary>Biçim ayrıntısı: ayraç, kodlama, satır grubu…</summary>
    public string Details { get; set; } = string.Empty;
}

/// <summary>CSV/TSV ve Parquet dosyalarının ilk satırlarını okur.</summary>
public static class TabularPreview
{
    public const int MaxRows = 300;
    public const int MaxColumns = 60;
    private const int MaxCellLength = 200;

    public static bool IsTabular(string extension) =>
        extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".tsv", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".tab", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".parquet", StringComparison.OrdinalIgnoreCase);

    public static Task<TableData> ReadAsync(string path, CancellationToken token = default) =>
        Path.GetExtension(path).Equals(".parquet", StringComparison.OrdinalIgnoreCase)
            ? ReadParquetAsync(path, token)
            : Task.Run(() => ReadDelimited(path, token), token);

    // ---------------------------------------------------------------- CSV / TSV

    private static TableData ReadDelimited(string path, CancellationToken token)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        var encoding = DetectEncoding(stream, out int preamble);
        stream.Position = preamble;

        // Ayraç tahmini için ilk birkaç KB yeterli.
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false, 1 << 16, leaveOpen: true);
        var head = new char[32 * 1024];
        int headLength = reader.ReadBlock(head, 0, head.Length);
        string sample = new(head, 0, headLength);
        char delimiter = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase)
            ? DetectDelimiter(sample)
            : '\t';

        stream.Position = preamble;
        reader.DiscardBufferedData();

        var data = new TableData();
        var records = ParseRecords(reader, delimiter, MaxRows + 1, token);
        if (records.Count == 0)
        {
            data.Details = "Boş dosya";
            return data;
        }

        int width = Math.Min(MaxColumns, records.Max(r => r.Count));
        var header = records[0];
        bool hasHeader = LooksLikeHeader(records);
        for (int c = 0; c < width; c++)
        {
            string name = hasHeader && c < header.Count && header[c].Trim().Length > 0 ? header[c].Trim() : $"Sütun {c + 1}";
            data.Columns.Add(name);
        }
        foreach (var record in records.Skip(hasHeader ? 1 : 0).Take(MaxRows))
        {
            var row = new string[width];
            for (int c = 0; c < width; c++) row[c] = c < record.Count ? Clean(record[c]) : string.Empty;
            data.Rows.Add(row);
        }
        for (int c = 0; c < width; c++) data.ColumnTypes.Add(GuessType(data.Rows, c));

        // Küçük dosyalarda toplam satırı say (tırnak içi satır sonları nadir; yaklaşık değer yeterli).
        if (stream.Length <= 64L * 1024 * 1024)
        {
            stream.Position = preamble;
            reader.DiscardBufferedData();
            long lines = CountRecords(reader, token);
            data.TotalRows = Math.Max(0, lines - (hasHeader ? 1 : 0));
        }

        string delimiterName = delimiter switch
        {
            '\t' => "sekme",
            ';' => "noktalı virgül",
            '|' => "dikey çizgi",
            _ => "virgül",
        };
        data.Details = $"Ayraç: {delimiterName} · {encoding.WebName.ToUpperInvariant()}" + (hasHeader ? string.Empty : " · başlık satırı yok");
        return data;
    }

    private static Encoding DetectEncoding(Stream stream, out int preamble)
    {
        var bom = new byte[4];
        int read = stream.Read(bom, 0, 4);
        preamble = 0;
        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) { preamble = 3; return new UTF8Encoding(false); }
        if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) { preamble = 2; return Encoding.Unicode; }
        if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) { preamble = 2; return Encoding.BigEndianUnicode; }

        // BOM yoksa: geçerli UTF-8 mi? Değilse sistemin ANSI kod sayfası (Türkçe Windows'ta 1254).
        stream.Position = 0;
        var buffer = new byte[256 * 1024];
        int length = stream.Read(buffer, 0, buffer.Length);
        try
        {
            // Son karakter yarım kalmış olabilir; birkaç baytı kırp.
            int safe = length;
            while (safe > 0 && safe > length - 4 && (buffer[safe - 1] & 0xC0) == 0x80) safe--;
            if (safe > 0 && buffer[safe - 1] >= 0xC0) safe--;
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetCharCount(buffer, 0, safe);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            try
            {
                return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
            }
            catch
            {
                return Encoding.Latin1;
            }
        }
    }

    private static char DetectDelimiter(string sample)
    {
        var candidates = new[] { ',', ';', '\t', '|' };
        var lines = sample.Split('\n').Take(20).Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count > 1) lines.RemoveAt(lines.Count - 1); // son satır yarım olabilir
        char best = ',';
        double bestScore = 0;
        foreach (char candidate in candidates)
        {
            var counts = lines.Select(l => CountOutsideQuotes(l, candidate)).ToList();
            if (counts.Count == 0 || counts[0] == 0) continue;
            int first = counts[0];
            double consistency = counts.Count(c => c == first) / (double)counts.Count;
            double score = consistency * 10 + Math.Min(first, 50) / 50.0;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }
        return best;
    }

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        int count = 0;
        bool quoted = false;
        foreach (char ch in line)
        {
            if (ch == '"') quoted = !quoted;
            else if (ch == delimiter && !quoted) count++;
        }
        return count;
    }

    /// <summary>RFC 4180: tırnaklı alanlar, kaçırılmış tırnaklar ("") ve alan içi satır sonları.</summary>
    private static List<List<string>> ParseRecords(TextReader reader, char delimiter, int maxRecords, CancellationToken token)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, fieldStarted = false;
        long consumed = 0;

        int next;
        while ((next = reader.Read()) != -1)
        {
            if ((++consumed & 0xFFFF) == 0) token.ThrowIfCancellationRequested();
            char ch = (char)next;
            if (quoted)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                    else quoted = false;
                }
                else if (field.Length < MaxCellLength * 4) field.Append(ch);
                continue;
            }

            if (ch == '"' && field.Length == 0 && !fieldStarted) { quoted = true; fieldStarted = true; }
            else if (ch == delimiter) { record.Add(field.ToString()); field.Clear(); fieldStarted = false; }
            else if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && reader.Peek() == '\n') reader.Read();
                record.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
                if (record.Count > 1 || record[0].Length > 0) records.Add(record);
                record = new List<string>();
                if (records.Count >= maxRecords) return records;
            }
            else
            {
                fieldStarted = true;
                if (field.Length < MaxCellLength * 4) field.Append(ch);
            }

            // Çok uzun tek satırlık dosyalara karşı sınır.
            if (consumed > 32L * 1024 * 1024) break;
        }
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            if (record.Count > 1 || record[0].Length > 0) records.Add(record);
        }
        return records;
    }

    private static long CountRecords(TextReader reader, CancellationToken token)
    {
        var buffer = new char[1 << 16];
        long lines = 0;
        int read;
        char last = '\n';
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            for (int i = 0; i < read; i++)
                if (buffer[i] == '\n') lines++;
            last = buffer[read - 1];
        }
        if (last != '\n') lines++;
        return lines;
    }

    /// <summary>İlk satır metin, alttakiler çoğunlukla sayı ise ya da hiç tekrar yoksa başlık sayılır.</summary>
    private static bool LooksLikeHeader(List<List<string>> records)
    {
        if (records.Count < 2) return true;
        var header = records[0];
        if (header.Any(h => h.Trim().Length == 0) && header.Count > 1 && header.Count(h => h.Trim().Length == 0) > header.Count / 2) return false;
        int numericHeader = header.Count(h => double.TryParse(h, NumberStyles.Any, CultureInfo.InvariantCulture, out _));
        return numericHeader == 0 || numericHeader < header.Count / 2;
    }

    private static string GuessType(List<string[]> rows, int column)
    {
        int numbers = 0, dates = 0, filled = 0;
        foreach (var row in rows.Take(100))
        {
            var value = row[column].Trim();
            if (value.Length == 0) continue;
            filled++;
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _) ||
                double.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out _)) numbers++;
            else if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out _) ||
                     DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) dates++;
        }
        if (filled == 0) return "boş";
        if (numbers == filled) return "sayı";
        if (dates == filled) return "tarih";
        return "metin";
    }

    private static string Clean(string value)
    {
        if (value.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0)
            value = value.Replace("\r\n", " ⏎ ").Replace('\n', '⏎').Replace('\r', ' ').Replace('\t', ' ');
        return value.Length > MaxCellLength ? value[..MaxCellLength] + "…" : value;
    }

    // ---------------------------------------------------------------- Parquet

    private static async Task<TableData> ReadParquetAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
        using var reader = await ParquetReader.CreateAsync(stream, cancellationToken: token);

        var data = new TableData();
        var fields = reader.Schema.GetDataFields();
        var shown = fields.Take(MaxColumns).ToArray();
        foreach (var field in shown)
        {
            data.Columns.Add(field.Name);
            data.ColumnTypes.Add(TypeName(field));
        }

        long total = 0;
        for (int g = 0; g < reader.RowGroupCount; g++)
        {
            using var group = reader.OpenRowGroupReader(g);
            total += group.RowCount;
        }
        data.TotalRows = total;

        // Satırları satır gruplarından sırayla, MaxRows'a kadar topla.
        for (int g = 0; g < reader.RowGroupCount && data.Rows.Count < MaxRows; g++)
        {
            token.ThrowIfCancellationRequested();
            using var group = reader.OpenRowGroupReader(g);
            int take = (int)Math.Min(group.RowCount, MaxRows - data.Rows.Count);
            var columns = new Array?[shown.Length];
            for (int c = 0; c < shown.Length; c++)
            {
                try
                {
                    var column = await group.ReadColumnAsync(shown[c], token);
                    // İç içe/tekrarlı alanlarda değer sayısı satır sayısına eşit olmaz; o sütunu atla.
                    columns[c] = column.Data.Length == group.RowCount ? column.Data : null;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    columns[c] = null;
                }
            }
            for (int r = 0; r < take; r++)
            {
                var row = new string[shown.Length];
                for (int c = 0; c < shown.Length; c++)
                    row[c] = columns[c] is { } values ? FormatValue(values.GetValue(r)) : "…";
                data.Rows.Add(row);
            }
        }

        string extra = fields.Length > MaxColumns ? $" · {fields.Length} sütundan ilk {MaxColumns}" : string.Empty;
        data.Details = $"Parquet · {reader.RowGroupCount} satır grubu{extra}";
        return data;
    }

    private static string TypeName(DataField field)
    {
        var type = Nullable.GetUnderlyingType(field.ClrType) ?? field.ClrType;
        return type switch
        {
            _ when type == typeof(string) => "metin",
            _ when type == typeof(bool) => "mantıksal",
            _ when type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) => "tarih",
            _ when type == typeof(TimeSpan) || type == typeof(TimeOnly) => "süre",
            _ when type == typeof(byte[]) => "ikili",
            _ when type == typeof(decimal) || type == typeof(double) || type == typeof(float) => "ondalık",
            _ when type.IsPrimitive => "tam sayı",
            _ => type.Name,
        };
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "—",
        string s => Clean(s),
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
        double d => d.ToString("G10", CultureInfo.CurrentCulture),
        float f => f.ToString("G7", CultureInfo.CurrentCulture),
        decimal m => m.ToString(CultureInfo.CurrentCulture),
        byte[] bytes => $"‹{bytes.Length} bayt›",
        bool b => b ? "doğru" : "yanlış",
        IFormattable f => f.ToString(null, CultureInfo.CurrentCulture),
        _ => Clean(value.ToString() ?? string.Empty),
    };
}
