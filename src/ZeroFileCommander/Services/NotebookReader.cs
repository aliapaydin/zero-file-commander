using System.Text.Json;

namespace ZeroFileCommander.Services;

public sealed record NotebookOutput(string? Text, string? PngBase64, bool IsError);

public sealed record NotebookCell(bool IsCode, bool IsMarkdown, string Source, int? ExecutionCount, IReadOnlyList<NotebookOutput> Outputs);

public sealed class Notebook
{
    public string Language { get; set; } = "python";
    public string? KernelName { get; set; }
    public List<NotebookCell> Cells { get; } = new();
    public int TotalCells { get; set; }
}

/// <summary>Jupyter (.ipynb) not defterlerini okur: hücreler, çalışma sırası, metin ve PNG çıktıları.</summary>
public static class NotebookReader
{
    public const int MaxCells = 200;
    private const int MaxOutputChars = 4000;
    private const int MaxImageBase64 = 4 * 1024 * 1024;

    public static Task<Notebook> ReadAsync(string path) => Task.Run(() =>
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
        var root = doc.RootElement;
        var notebook = new Notebook();

        if (root.TryGetProperty("metadata", out var metadata))
        {
            if (metadata.TryGetProperty("kernelspec", out var kernel))
            {
                notebook.KernelName = Str(kernel, "display_name") ?? Str(kernel, "name");
                if (Str(kernel, "language") is { } kernelLanguage) notebook.Language = kernelLanguage;
            }
            if (metadata.TryGetProperty("language_info", out var info) && Str(info, "name") is { } languageName)
                notebook.Language = languageName;
        }

        if (!root.TryGetProperty("cells", out var cells) || cells.ValueKind != JsonValueKind.Array) return notebook;
        notebook.TotalCells = cells.GetArrayLength();
        foreach (var cell in cells.EnumerateArray().Take(MaxCells))
        {
            string type = Str(cell, "cell_type") ?? "code";
            string source = Text(cell, "source");
            int? count = cell.TryGetProperty("execution_count", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;
            var outputs = new List<NotebookOutput>();
            if (type == "code" && cell.TryGetProperty("outputs", out var outs) && outs.ValueKind == JsonValueKind.Array)
            {
                foreach (var output in outs.EnumerateArray())
                {
                    switch (Str(output, "output_type"))
                    {
                        case "stream":
                            outputs.Add(new NotebookOutput(Limit(Text(output, "text")), null, Str(output, "name") == "stderr"));
                            break;
                        case "error":
                            var trace = output.TryGetProperty("traceback", out var tb) && tb.ValueKind == JsonValueKind.Array
                                ? string.Join("\n", tb.EnumerateArray().Select(t => StripAnsi(t.GetString() ?? string.Empty)))
                                : $"{Str(output, "ename")}: {Str(output, "evalue")}";
                            outputs.Add(new NotebookOutput(Limit(trace), null, true));
                            break;
                        case "execute_result":
                        case "display_data":
                            if (!output.TryGetProperty("data", out var data)) break;
                            if (data.TryGetProperty("image/png", out var png))
                            {
                                var base64 = png.ValueKind == JsonValueKind.Array
                                    ? string.Concat(png.EnumerateArray().Select(p => p.GetString()))
                                    : png.GetString();
                                if (base64 is not null && base64.Length < MaxImageBase64)
                                {
                                    outputs.Add(new NotebookOutput(null, base64.Replace("\n", string.Empty), false));
                                    break;
                                }
                            }
                            if (data.TryGetProperty("text/plain", out _))
                                outputs.Add(new NotebookOutput(Limit(Text(data, "text/plain")), null, false));
                            else if (data.TryGetProperty("text/html", out _))
                                outputs.Add(new NotebookOutput("‹HTML çıktısı (tablo/grafik) önizlemede gösterilmiyor›", null, false));
                            break;
                    }
                }
            }
            notebook.Cells.Add(new NotebookCell(type == "code", type == "markdown", source.TrimEnd(), count, outputs));
        }
        return notebook;
    });

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>ipynb'de metinler tek dize ya da satır dizisi olabilir.</summary>
    private static string Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Concat(value.EnumerateArray().Select(v => v.GetString() ?? string.Empty)),
            _ => string.Empty,
        };
    }

    private static string Limit(string text)
    {
        text = text.TrimEnd();
        return text.Length > MaxOutputChars ? text[..MaxOutputChars] + "\n…" : text;
    }

    private static string StripAnsi(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\x1B\[[0-9;]*[A-Za-z]", string.Empty);
}
