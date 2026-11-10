using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZeroFileCommander.Services;

/// <summary>Önizlemede gösterilen bir satır: ad, kısa açıklama ve isteğe bağlı kod (DAX, M).</summary>
public sealed record MetaEntry(string Title, string? Detail = null, string? Code = null);

public sealed class MetaSection
{
    public MetaSection(string title, string glyph)
    {
        Title = title;
        Glyph = glyph;
    }

    public string Title { get; }
    public string Glyph { get; }
    public List<MetaEntry> Entries { get; } = new();
}

/// <summary>Bir Power BI dosyası/projesinin özeti.</summary>
public sealed class MetaReport
{
    /// <summary>Üstte gösterilen kısa sayılar: "8 tablo", "124 ölçü"…</summary>
    public List<string> Badges { get; } = new();
    public List<MetaSection> Sections { get; } = new();
    public List<string> Notes { get; } = new();

    public MetaSection Section(string title, string glyph)
    {
        var existing = Sections.FirstOrDefault(s => s.Title == title);
        if (existing is not null) return existing;
        var section = new MetaSection(title, glyph);
        Sections.Add(section);
        return section;
    }
}

/// <summary>
/// PBIX/PBIT dosyalarını (ZIP) ve PBIP projelerini (klasör: .Report + .SemanticModel, TMSL veya TMDL)
/// yalnızca okuyarak inceler: sayfalar, görseller, tablolar, ölçüler, ilişkiler, Power Query sorguları.
/// </summary>
public static class PowerBiInspector
{
    private const int MaxEntries = 400;

    public static bool CanInspect(string path, bool isDirectory)
    {
        if (isDirectory)
            return path.EndsWith(".SemanticModel", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".Dataset", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".Report", StringComparison.OrdinalIgnoreCase);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".pbix" or ".pbit" or ".pbip" ||
               Path.GetFileName(path).Equals("model.bim", StringComparison.OrdinalIgnoreCase);
    }

    public static Task<MetaReport> InspectAsync(string path, bool isDirectory, CancellationToken token = default) =>
        Task.Run(() =>
        {
            var report = new MetaReport();
            if (isDirectory)
            {
                if (path.EndsWith(".Report", StringComparison.OrdinalIgnoreCase)) ReadReportFolder(path, report);
                else ReadSemanticModelFolder(path, report);
            }
            else
            {
                switch (Path.GetExtension(path).ToLowerInvariant())
                {
                    case ".pbix":
                    case ".pbit":
                        ReadPbix(path, report, token);
                        break;
                    case ".pbip":
                        ReadPbip(path, report);
                        break;
                    default:
                        ReadModelJson(File.ReadAllText(path), report);
                        break;
                }
            }
            Finish(report);
            return report;
        }, token);

    // ---------------------------------------------------------------- PBIX / PBIT

    private static void ReadPbix(string path, MetaReport report, CancellationToken token)
    {
        using var zip = ZipFile.OpenRead(path);
        var entries = zip.Entries
            .GroupBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var file = report.Section("Dosya", "");
        if (entries.TryGetValue("Version", out var version))
            file.Entries.Add(new MetaEntry("Biçim sürümü", ReadUtf16(version).Trim()));
        if (entries.TryGetValue("Metadata", out var metadata))
        {
            try
            {
                using var doc = JsonDocument.Parse(ReadUtf16(metadata));
                if (Str(doc.RootElement, "CreatedFrom") is { } from)
                    file.Entries.Add(new MetaEntry("Oluşturulduğu yer", from + (Str(doc.RootElement, "CreatedFromRelease") is { } rel ? $" ({rel})" : "")));
            }
            catch (JsonException) { }
        }
        if (entries.TryGetValue("DataModel", out var dataModel))
        {
            file.Entries.Add(new MetaEntry("Veri modeli", $"{Models.FileItem.FormatSize(dataModel.Length)} (sıkıştırılmış)"));
            report.Notes.Add("PBIX içindeki veri modeli ikili ve sıkıştırılmış olduğundan tablo/ölçü listesi okunamıyor. Ayrıntı için dosyayı PBIT şablonu veya PBIP projesi olarak kaydedin.");
        }

        if (entries.TryGetValue("Report/Layout", out var layout))
            ReadLayout(ReadUtf16(layout), report);

        if (entries.TryGetValue("DataModelSchema", out var schema))
            ReadModelJson(ReadUtf16(schema), report);

        if (entries.TryGetValue("Connections", out var connections))
            ReadConnections(ReadText(connections), report);

        token.ThrowIfCancellationRequested();
        if (entries.TryGetValue("DataMashup", out var mashup) && !report.Sections.Any(s => s.Title == "Power Query"))
            ReadDataMashup(mashup, report);
    }

    /// <summary>Report/Layout: sayfalar ve her sayfadaki görsel türleri.</summary>
    private static void ReadLayout(string json, MetaReport report)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Array) return;
            var pages = report.Section("Sayfalar", "");
            int visualTotal = 0;
            foreach (var section in sections.EnumerateArray().OrderBy(s => Int(s, "ordinal") ?? 0))
            {
                var types = new List<string>();
                if (section.TryGetProperty("visualContainers", out var visuals) && visuals.ValueKind == JsonValueKind.Array)
                {
                    foreach (var visual in visuals.EnumerateArray())
                    {
                        if (Str(visual, "config") is not { } config) continue;
                        try
                        {
                            using var cfg = JsonDocument.Parse(config);
                            if (cfg.RootElement.TryGetProperty("singleVisual", out var single) && Str(single, "visualType") is { } type)
                                types.Add(type);
                        }
                        catch (JsonException) { }
                    }
                }
                visualTotal += types.Count;
                pages.Entries.Add(new MetaEntry(Str(section, "displayName") ?? Str(section, "name") ?? "Sayfa", DescribeVisuals(types)));
            }
            report.Badges.Add($"{pages.Entries.Count} sayfa");
            if (visualTotal > 0) report.Badges.Add($"{visualTotal} görsel");
        }
        catch (JsonException) { }
    }

    private static void ReadConnections(string json, MetaReport report)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var section = report.Section("Bağlantılar", "");
            if (doc.RootElement.TryGetProperty("Connections", out var connections) && connections.ValueKind == JsonValueKind.Array)
            {
                foreach (var connection in connections.EnumerateArray())
                {
                    string type = Str(connection, "ConnectionType") ?? "bağlantı";
                    string name = Str(connection, "Name") ?? type;
                    section.Entries.Add(new MetaEntry(name, type switch
                    {
                        "pbiServiceLive" => "Power BI hizmetindeki anlam modeline canlı bağlantı",
                        "analysisServicesDatabaseLive" => "Analysis Services'e canlı bağlantı",
                        "pbiServiceXmlaStyleLive" => "XMLA canlı bağlantı",
                        _ => type,
                    }, Str(connection, "ConnectionString")));
                }
            }
            if (doc.RootElement.TryGetProperty("RemoteArtifacts", out var remote) && remote.ValueKind == JsonValueKind.Array)
            {
                foreach (var artifact in remote.EnumerateArray())
                    if (Str(artifact, "ReportId") is { } reportId)
                        section.Entries.Add(new MetaEntry("Yayımlanmış rapor", reportId));
            }
            if (section.Entries.Count == 0) report.Sections.Remove(section);
        }
        catch (JsonException) { }
    }

    /// <summary>
    /// DataMashup: 4 bayt sürüm + 4 bayt uzunluk + iç ZIP (Formulas/Section1.m). Power Query sorgularını verir.
    /// </summary>
    private static void ReadDataMashup(ZipArchiveEntry entry, MetaReport report)
    {
        try
        {
            using var raw = entry.Open();
            using var memory = new MemoryStream();
            raw.CopyTo(memory);
            var bytes = memory.GetBuffer();
            if (memory.Length < 8) return;
            int length = BitConverter.ToInt32(bytes, 4);
            if (length <= 0 || 8 + (long)length > memory.Length) return;
            using var inner = new ZipArchive(new MemoryStream(bytes, 8, length, writable: false), ZipArchiveMode.Read);
            var formulas = inner.GetEntry("Formulas/Section1.m");
            if (formulas is null) return;
            ReadPowerQuery(ReadText(formulas), report);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            // Eski/farklı biçim: sessizce geç
        }
    }

    private static readonly Regex SharedQuery = new(
        @"^shared\s+(?:#""(?<name>(?:[^""]|"""")+)""|(?<name>[\w\.]+))\s*=\s*",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static void ReadPowerQuery(string section1, MetaReport report)
    {
        var matches = SharedQuery.Matches(section1);
        if (matches.Count == 0) return;
        var queries = report.Section("Power Query", "");
        for (int i = 0; i < matches.Count && queries.Entries.Count < MaxEntries; i++)
        {
            int start = matches[i].Index + matches[i].Length;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : section1.Length;
            string body = section1[start..end].Trim().TrimEnd(';').Trim();
            string name = matches[i].Groups["name"].Value.Replace("\"\"", "\"");
            queries.Entries.Add(new MetaEntry(name, DescribeSource(body), body));
        }
        report.Badges.Add($"{queries.Entries.Count} sorgu");
    }

    private static string? DescribeSource(string m)
    {
        var sources = new (string Pattern, string Label)[]
        {
            ("Sql.Database", "SQL Server"), ("Sql.Databases", "SQL Server"), ("Excel.Workbook", "Excel"),
            ("Csv.Document", "CSV"), ("SharePoint.", "SharePoint"), ("Web.Contents", "Web"), ("OData.Feed", "OData"),
            ("Folder.Files", "Klasör"), ("Oracle.Database", "Oracle"), ("PostgreSQL.Database", "PostgreSQL"),
            ("MySQL.Database", "MySQL"), ("Snowflake.", "Snowflake"), ("GoogleBigQuery.", "BigQuery"),
            ("Databricks.", "Databricks"), ("AzureStorage.", "Azure Storage"), ("Json.Document", "JSON"),
            ("Parquet.Document", "Parquet"), ("PowerPlatform.Dataflows", "Dataflow"), ("Lakehouse.", "Lakehouse"),
            ("Table.FromRows", "Girilen veri"),
        };
        var found = sources.Where(s => m.Contains(s.Pattern, StringComparison.Ordinal)).Select(s => s.Label).Distinct().ToList();
        return found.Count > 0 ? string.Join(", ", found) : null;
    }

    // ---------------------------------------------------------------- Model (TMSL JSON: model.bim / DataModelSchema)

    private static void ReadModelJson(string json, MetaReport report)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        var model = root.TryGetProperty("model", out var m) ? m : root;

        var tables = report.Section("Tablolar", "");
        var measures = report.Section("Ölçüler", "");
        var calculated = report.Section("Hesaplanmış sütunlar", "");
        int autoDate = 0;
        var queries = new List<(string Name, string Code)>();

        if (model.TryGetProperty("tables", out var tableArray) && tableArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var table in tableArray.EnumerateArray())
            {
                string name = Str(table, "name") ?? "?";
                if (name.StartsWith("DateTableTemplate_", StringComparison.Ordinal) || name.StartsWith("LocalDateTable_", StringComparison.Ordinal))
                {
                    autoDate++;
                    continue;
                }
                int columnCount = 0;
                if (table.TryGetProperty("columns", out var columns) && columns.ValueKind == JsonValueKind.Array)
                {
                    foreach (var column in columns.EnumerateArray())
                    {
                        if (Str(column, "type") == "rowNumber") continue;
                        columnCount++;
                        if (Text(column, "expression") is { } expression && calculated.Entries.Count < MaxEntries)
                            calculated.Entries.Add(new MetaEntry(Str(column, "name") ?? "?", name, expression));
                    }
                }
                int measureCount = 0;
                if (table.TryGetProperty("measures", out var measureArray) && measureArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var measure in measureArray.EnumerateArray())
                    {
                        measureCount++;
                        if (measures.Entries.Count < MaxEntries)
                            measures.Entries.Add(new MetaEntry(Str(measure, "name") ?? "?", name, Text(measure, "expression")));
                    }
                }
                string? mode = null;
                if (table.TryGetProperty("partitions", out var partitions) && partitions.ValueKind == JsonValueKind.Array)
                {
                    foreach (var partition in partitions.EnumerateArray())
                    {
                        mode ??= Str(partition, "mode");
                        if (partition.TryGetProperty("source", out var source))
                        {
                            string? type = Str(source, "type");
                            if (Text(source, "expression") is { } code)
                                queries.Add((name, code));
                            if (type == "calculated") mode = "hesaplanmış tablo";
                        }
                    }
                }
                bool hidden = table.TryGetProperty("isHidden", out var h) && h.ValueKind == JsonValueKind.True;
                tables.Entries.Add(new MetaEntry(name, Join(
                    Plural(columnCount, "sütun"), measureCount > 0 ? Plural(measureCount, "ölçü") : null,
                    mode is null or "import" ? null : mode, hidden ? "gizli" : null)));
            }
        }

        if (model.TryGetProperty("relationships", out var relationships) && relationships.ValueKind == JsonValueKind.Array)
        {
            var section = report.Section("İlişkiler", "");
            foreach (var relation in relationships.EnumerateArray())
            {
                bool inactive = relation.TryGetProperty("isActive", out var active) && active.ValueKind == JsonValueKind.False;
                bool both = Str(relation, "crossFilteringBehavior") == "bothDirections";
                section.Entries.Add(new MetaEntry(
                    $"{Str(relation, "fromTable")}[{Str(relation, "fromColumn")}] → {Str(relation, "toTable")}[{Str(relation, "toColumn")}]",
                    Join(both ? "çift yönlü" : null, inactive ? "pasif" : null)));
            }
        }

        if (queries.Count > 0 && !report.Sections.Any(s => s.Title == "Power Query"))
        {
            var section = report.Section("Power Query", "");
            foreach (var (name, code) in queries.Take(MaxEntries))
                section.Entries.Add(new MetaEntry(name, DescribeSource(code), code));
        }

        if (autoDate > 0)
            report.Notes.Add($"{autoDate} otomatik tarih tablosu gizlendi (Seçenekler → Otomatik tarih/saat).");
        AddModelBadges(report, tables.Entries.Count, measures.Entries.Count,
            report.Sections.FirstOrDefault(s => s.Title == "İlişkiler")?.Entries.Count ?? 0);
        if (Str(model, "culture") is { } culture)
            report.Section("Dosya", "").Entries.Add(new MetaEntry("Model kültürü", culture));
        if (root.TryGetProperty("compatibilityLevel", out var level) && level.ValueKind == JsonValueKind.Number)
            report.Section("Dosya", "").Entries.Add(new MetaEntry("Uyumluluk düzeyi", level.GetRawText()));
    }

    // ---------------------------------------------------------------- PBIP

    private static void ReadPbip(string path, MetaReport report)
    {
        var folder = Path.GetDirectoryName(path)!;
        string? reportPath = null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("artifacts", out var artifacts) && artifacts.ValueKind == JsonValueKind.Array)
                foreach (var artifact in artifacts.EnumerateArray())
                    if (artifact.TryGetProperty("report", out var r) && Str(r, "path") is { } p)
                        reportPath = Path.GetFullPath(Path.Combine(folder, p));
        }
        catch (JsonException) { }

        reportPath ??= Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + ".Report");
        report.Section("Dosya", "").Entries.Add(new MetaEntry("Power BI projesi (PBIP)", Path.GetFileName(reportPath)));
        if (Directory.Exists(reportPath)) ReadReportFolder(reportPath, report);
        else report.Notes.Add("Rapor klasörü bulunamadı: " + reportPath);
    }

    private static void ReadReportFolder(string reportFolder, MetaReport report)
    {
        // Sayfalar: eski tek dosya (report.json) ya da PBIR (definition/pages/*)
        var legacy = Path.Combine(reportFolder, "report.json");
        var pagesFolder = Path.Combine(reportFolder, "definition", "pages");
        if (File.Exists(legacy)) ReadLayout(File.ReadAllText(legacy), report);
        else if (Directory.Exists(pagesFolder)) ReadPbirPages(pagesFolder, report);

        // Anlam modeli: definition.pbir → datasetReference
        string? modelFolder = null;
        var pbir = Path.Combine(reportFolder, "definition.pbir");
        if (File.Exists(pbir))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(pbir));
                if (doc.RootElement.TryGetProperty("datasetReference", out var reference))
                {
                    if (reference.TryGetProperty("byPath", out var byPath) && Str(byPath, "path") is { } relative)
                        modelFolder = Path.GetFullPath(Path.Combine(reportFolder, relative));
                    else if (reference.TryGetProperty("byConnection", out var byConnection))
                        report.Section("Bağlantılar", "").Entries.Add(new MetaEntry("Uzak anlam modeli",
                            Str(byConnection, "pbiModelDatabaseName") ?? Str(byConnection, "connectionString") ?? "canlı bağlantı",
                            Str(byConnection, "connectionString")));
                }
            }
            catch (JsonException) { }
        }
        if (modelFolder is null)
        {
            var baseName = Path.GetFileName(reportFolder);
            baseName = baseName[..^".Report".Length];
            var parent = Path.GetDirectoryName(reportFolder)!;
            modelFolder = new[] { ".SemanticModel", ".Dataset" }.Select(s => Path.Combine(parent, baseName + s)).FirstOrDefault(Directory.Exists);
        }
        if (modelFolder is not null && Directory.Exists(modelFolder)) ReadSemanticModelFolder(modelFolder, report);
    }

    private static void ReadPbirPages(string pagesFolder, MetaReport report)
    {
        var order = new List<string>();
        var pagesJson = Path.Combine(pagesFolder, "pages.json");
        if (File.Exists(pagesJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(pagesJson));
                if (doc.RootElement.TryGetProperty("pageOrder", out var pageOrder) && pageOrder.ValueKind == JsonValueKind.Array)
                    order.AddRange(pageOrder.EnumerateArray().Select(p => p.GetString() ?? string.Empty));
            }
            catch (JsonException) { }
        }
        var folders = Directory.GetDirectories(pagesFolder)
            .OrderBy(d => { int i = order.IndexOf(Path.GetFileName(d)); return i < 0 ? int.MaxValue : i; })
            .ToList();
        var pages = report.Section("Sayfalar", "");
        int visualTotal = 0;
        foreach (var folder in folders)
        {
            string name = Path.GetFileName(folder);
            var pageFile = Path.Combine(folder, "page.json");
            if (File.Exists(pageFile))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(pageFile));
                    name = Str(doc.RootElement, "displayName") ?? name;
                }
                catch (JsonException) { }
            }
            var types = new List<string>();
            var visualsFolder = Path.Combine(folder, "visuals");
            if (Directory.Exists(visualsFolder))
            {
                foreach (var visualFile in Directory.EnumerateFiles(visualsFolder, "visual.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(visualFile));
                        if (doc.RootElement.TryGetProperty("visual", out var visual) && Str(visual, "visualType") is { } type)
                            types.Add(type);
                    }
                    catch (JsonException) { }
                }
            }
            visualTotal += types.Count;
            pages.Entries.Add(new MetaEntry(name, DescribeVisuals(types)));
        }
        report.Badges.Add($"{pages.Entries.Count} sayfa");
        if (visualTotal > 0) report.Badges.Add($"{visualTotal} görsel");
    }

    private static void ReadSemanticModelFolder(string folder, MetaReport report)
    {
        var bim = Path.Combine(folder, "model.bim");
        var definition = Path.Combine(folder, "definition");
        if (File.Exists(bim)) ReadModelJson(File.ReadAllText(bim), report);
        else if (Directory.Exists(definition)) ReadTmdl(definition, report);
        else report.Notes.Add("Anlam modeli tanımı bulunamadı (model.bim veya definition/).");
    }

    // ---------------------------------------------------------------- TMDL

    private static void ReadTmdl(string definition, MetaReport report)
    {
        var tables = report.Section("Tablolar", "");
        var measures = report.Section("Ölçüler", "");
        var calculated = report.Section("Hesaplanmış sütunlar", "");
        var queries = new List<(string Name, string Code)>();
        int autoDate = 0;

        var tablesFolder = Path.Combine(definition, "tables");
        if (Directory.Exists(tablesFolder))
        {
            foreach (var file in Directory.EnumerateFiles(tablesFolder, "*.tmdl").Order(StringComparer.OrdinalIgnoreCase))
            {
                var lines = File.ReadAllLines(file);
                string tableName = Path.GetFileNameWithoutExtension(file);
                int columns = 0, measureCount = 0;
                string? mode = null;
                bool hidden = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    var (indent, text) = Split(lines[i]);
                    if (indent == 0 && text.StartsWith("table ", StringComparison.Ordinal))
                    {
                        tableName = Unquote(text[6..].Trim());
                    }
                    else if (indent == 1 && text == "isHidden")
                    {
                        hidden = true;
                    }
                    else if (indent == 1 && (text.StartsWith("measure ", StringComparison.Ordinal) || text.StartsWith("column ", StringComparison.Ordinal)))
                    {
                        bool isMeasure = text.StartsWith("measure ", StringComparison.Ordinal);
                        var (name, expression) = NameAndExpression(text[(isMeasure ? 8 : 7)..]);
                        if (expression is not null && expression.Length == 0) expression = ReadBlock(lines, ref i, 1);
                        if (isMeasure)
                        {
                            measureCount++;
                            if (measures.Entries.Count < MaxEntries) measures.Entries.Add(new MetaEntry(name, tableName, expression));
                        }
                        else
                        {
                            columns++;
                            if (expression is not null && calculated.Entries.Count < MaxEntries)
                                calculated.Entries.Add(new MetaEntry(name, tableName, expression));
                        }
                    }
                    else if (indent == 1 && text.StartsWith("partition ", StringComparison.Ordinal))
                    {
                        // partition Ad = m / calculated
                        int eq = text.LastIndexOf('=');
                        string kind = eq > 0 ? text[(eq + 1)..].Trim() : string.Empty;
                        if (kind == "calculated") mode = "hesaplanmış tablo";
                    }
                    else if (indent == 2 && text.StartsWith("mode:", StringComparison.Ordinal))
                    {
                        mode ??= text[5..].Trim();
                    }
                    else if (indent == 2 && text.StartsWith("source", StringComparison.Ordinal) && text.TrimEnd().EndsWith('='))
                    {
                        var code = ReadBlock(lines, ref i, 2);
                        if (code.Length > 0) queries.Add((tableName, code));
                    }
                }

                if (tableName.StartsWith("DateTableTemplate_", StringComparison.Ordinal) || tableName.StartsWith("LocalDateTable_", StringComparison.Ordinal))
                {
                    autoDate++;
                    measures.Entries.RemoveAll(e => e.Detail == tableName);
                    calculated.Entries.RemoveAll(e => e.Detail == tableName);
                    queries.RemoveAll(q => q.Name == tableName);
                    continue;
                }
                tables.Entries.Add(new MetaEntry(tableName, Join(
                    Plural(columns, "sütun"), measureCount > 0 ? Plural(measureCount, "ölçü") : null,
                    mode is null or "import" ? null : mode, hidden ? "gizli" : null)));
            }
        }

        var relationshipsFile = Path.Combine(definition, "relationships.tmdl");
        int relationCount = 0;
        if (File.Exists(relationshipsFile))
        {
            var section = report.Section("İlişkiler", "");
            string? from = null, to = null;
            bool inactive = false, both = false, open = false;
            void Flush()
            {
                if (open && from is not null && to is not null)
                    section.Entries.Add(new MetaEntry($"{FormatColumnRef(from)} → {FormatColumnRef(to)}", Join(both ? "çift yönlü" : null, inactive ? "pasif" : null)));
                from = to = null;
                inactive = both = false;
            }
            foreach (var raw in File.ReadAllLines(relationshipsFile))
            {
                var (indent, text) = Split(raw);
                if (indent == 0 && text.StartsWith("relationship ", StringComparison.Ordinal)) { Flush(); open = true; }
                else if (text.StartsWith("fromColumn:", StringComparison.Ordinal)) from = text[11..].Trim();
                else if (text.StartsWith("toColumn:", StringComparison.Ordinal)) to = text[9..].Trim();
                else if (text.StartsWith("isActive:", StringComparison.Ordinal)) inactive = text.EndsWith("false", StringComparison.Ordinal);
                else if (text.StartsWith("crossFilteringBehavior:", StringComparison.Ordinal)) both = text.EndsWith("bothDirections", StringComparison.Ordinal);
            }
            Flush();
            relationCount = section.Entries.Count;
        }

        if (queries.Count > 0 && !report.Sections.Any(s => s.Title == "Power Query"))
        {
            var section = report.Section("Power Query", "");
            foreach (var (name, code) in queries.Take(MaxEntries))
                section.Entries.Add(new MetaEntry(name, DescribeSource(code), code));
        }

        // Paylaşılan ifadeler (parametreler, ortak sorgular)
        var expressions = Path.Combine(definition, "expressions.tmdl");
        if (File.Exists(expressions))
        {
            var lines = File.ReadAllLines(expressions);
            var section = report.Section("Parametreler ve ortak sorgular", "");
            for (int i = 0; i < lines.Length; i++)
            {
                var (indent, text) = Split(lines[i]);
                if (indent != 0 || !text.StartsWith("expression ", StringComparison.Ordinal)) continue;
                var (name, code) = NameAndExpression(text[11..]);
                if (code is not null && code.Length == 0) code = ReadBlock(lines, ref i, 0);
                // "… meta [IsParameterQuery=true]" kısmını sadeleştir
                if (code is not null)
                {
                    int meta = code.IndexOf(" meta [", StringComparison.Ordinal);
                    if (meta > 0 && code.Contains("IsParameterQuery", StringComparison.Ordinal)) code = code[..meta].Trim();
                }
                section.Entries.Add(new MetaEntry(name, DescribeSource(code ?? string.Empty), code));
            }
            if (section.Entries.Count == 0) report.Sections.Remove(section);
        }

        if (autoDate > 0)
            report.Notes.Add($"{autoDate} otomatik tarih tablosu gizlendi (Seçenekler → Otomatik tarih/saat).");
        AddModelBadges(report, tables.Entries.Count, measures.Entries.Count, relationCount);
    }

    /// <summary>Satırın girinti düzeyi (sekme veya 4 boşluk) ve kırpılmış metni.</summary>
    private static (int Indent, string Text) Split(string line)
    {
        int tabs = 0, spaces = 0, i = 0;
        for (; i < line.Length; i++)
        {
            if (line[i] == '\t') tabs++;
            else if (line[i] == ' ') spaces++;
            else break;
        }
        return (tabs + spaces / 4, line[i..].TrimEnd());
    }

    /// <summary>"'Toplam Satış' = SUM(...)" → (Toplam Satış, SUM(...)); ifade yoksa null, "=" ile bitiyorsa "".</summary>
    private static (string Name, string? Expression) NameAndExpression(string text)
    {
        text = text.Trim();
        string name;
        string rest;
        if (text.StartsWith('\''))
        {
            int end = 1;
            while (end < text.Length)
            {
                if (text[end] == '\'' && end + 1 < text.Length && text[end + 1] == '\'') { end += 2; continue; }
                if (text[end] == '\'') break;
                end++;
            }
            name = text[1..Math.Min(end, text.Length)].Replace("''", "'");
            rest = end + 1 < text.Length ? text[(end + 1)..].Trim() : string.Empty;
        }
        else
        {
            int eq = text.IndexOf('=');
            name = (eq >= 0 ? text[..eq] : text).Trim();
            rest = eq >= 0 ? text[eq..].Trim() : string.Empty;
        }
        if (!rest.StartsWith('=')) return (name, null);
        string expression = rest[1..].Trim();
        if (expression == "```") return (name, string.Empty);
        return (name, expression);
    }

    /// <summary>Girintisi <paramref name="ownerIndent"/>'ten derin olan devam satırlarını (ya da ``` bloğunu) okur.</summary>
    private static string ReadBlock(string[] lines, ref int index, int ownerIndent)
    {
        var builder = new StringBuilder();
        bool fenced = lines[index].TrimEnd().EndsWith("```", StringComparison.Ordinal);
        int baseIndent = -1;
        while (index + 1 < lines.Length)
        {
            var raw = lines[index + 1];
            var (indent, text) = Split(raw);
            if (fenced)
            {
                index++;
                if (text == "```") break;
            }
            else
            {
                if (text.Length > 0 && indent <= ownerIndent + 1) break;
                index++;
            }
            if (text.Length == 0) { builder.AppendLine(); continue; }
            if (baseIndent < 0) baseIndent = indent;
            builder.Append(new string(' ', Math.Max(0, indent - baseIndent) * 4)).AppendLine(text);
        }
        return builder.ToString().Trim();
    }

    private static string FormatColumnRef(string reference)
    {
        // Tablo.Sütun veya 'Tablo Adı'.'Sütun Adı'
        int dot = reference.StartsWith('\'') ? reference.IndexOf("'.", StringComparison.Ordinal) + 1 : reference.IndexOf('.');
        if (dot <= 0) return reference;
        return $"{Unquote(reference[..dot])}[{Unquote(reference[(dot + 1)..])}]";
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '\'' && value[^1] == '\'' ? value[1..^1].Replace("''", "'") : value;

    // ---------------------------------------------------------------- yardımcılar

    private static void AddModelBadges(MetaReport report, int tables, int measures, int relationships)
    {
        if (tables > 0) report.Badges.Insert(0, $"{tables} tablo");
        if (measures > 0) report.Badges.Add($"{measures} ölçü");
        if (relationships > 0) report.Badges.Add($"{relationships} ilişki");
    }

    private static void Finish(MetaReport report)
    {
        report.Sections.RemoveAll(s => s.Entries.Count == 0);
        // Okuma sırası: dosya bilgisi en sonda.
        var file = report.Sections.FirstOrDefault(s => s.Title == "Dosya");
        if (file is not null)
        {
            report.Sections.Remove(file);
            report.Sections.Add(file);
        }
        var distinct = report.Badges.Distinct().ToList();
        report.Badges.Clear();
        report.Badges.AddRange(distinct);
    }

    private static readonly Dictionary<string, string> VisualNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["card"] = "kart", ["multiRowCard"] = "çok satırlı kart", ["cardVisual"] = "kart", ["tableEx"] = "tablo",
        ["pivotTable"] = "matris", ["slicer"] = "dilimleyici", ["advancedSlicerVisual"] = "dilimleyici",
        ["barChart"] = "çubuk", ["clusteredBarChart"] = "çubuk", ["stackedBarChart"] = "yığılmış çubuk",
        ["columnChart"] = "sütun", ["clusteredColumnChart"] = "sütun", ["stackedColumnChart"] = "yığılmış sütun",
        ["lineChart"] = "çizgi", ["areaChart"] = "alan", ["lineClusteredColumnComboChart"] = "birleşik",
        ["lineStackedColumnComboChart"] = "birleşik", ["pieChart"] = "pasta", ["donutChart"] = "halka",
        ["map"] = "harita", ["filledMap"] = "dolgulu harita", ["azureMap"] = "Azure haritası", ["gauge"] = "gösterge",
        ["kpi"] = "KPI", ["textbox"] = "metin kutusu", ["image"] = "görüntü", ["shape"] = "şekil",
        ["actionButton"] = "düğme", ["scatterChart"] = "dağılım", ["treemap"] = "ağaç haritası",
        ["waterfallChart"] = "şelale", ["funnel"] = "huni", ["ribbonChart"] = "şerit", ["decompositionTreeVisual"] = "ayrıştırma ağacı",
        ["keyDriversVisual"] = "temel etkileyenler", ["qnaVisual"] = "Soru-Cevap", ["bookmarkNavigator"] = "yer imi gezgini",
        ["pageNavigator"] = "sayfa gezgini",
    };

    private static string? DescribeVisuals(List<string> types)
    {
        if (types.Count == 0) return "görsel yok";
        var groups = types
            .GroupBy(t => VisualNames.TryGetValue(t, out var label) ? label : t)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Count() > 1 ? $"{g.Count()} {g.Key}" : g.Key)
            .Take(6)
            .ToList();
        return $"{Plural(types.Count, "görsel")}: {string.Join(", ", groups)}";
    }

    private static string Plural(int count, string noun) => $"{count} {noun}";

    private static string? Join(params string?[] parts)
    {
        var list = parts.Where(p => !string.IsNullOrEmpty(p)).ToList();
        return list.Count > 0 ? string.Join(" · ", list) : null;
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int i)
            ? i
            : null;

    /// <summary>TMSL ifadeleri tek metin ya da satır dizisi olabilir.</summary>
    private static string? Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Array => string.Join("\n", value.EnumerateArray().Select(v => v.GetString() ?? string.Empty)).Trim(),
            _ => null,
        };
    }

    private static string ReadUtf16(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.Unicode, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd().TrimStart('﻿');
    }
}
