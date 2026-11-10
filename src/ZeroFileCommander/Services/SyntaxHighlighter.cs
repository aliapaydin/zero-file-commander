using System.Text.RegularExpressions;

namespace ZeroFileCommander.Services;

public enum TokenKind
{
    Plain,
    Comment,
    String,
    Keyword,
    Control,
    Number,
    Type,
    Function,
    Tag,
    Attribute,
    Property,
    Heading,
}

public readonly record struct Token(string Text, TokenKind Kind);

/// <summary>
/// Önizleme için hafif sözdizimi renklendirme: dil başına tek birleşik düzenli ifade.
/// Tam bir ayrıştırıcı değil; okunurluk için VS Code'a yakın renkler verir.
/// </summary>
public static class SyntaxHighlighter
{
    private sealed record Language(string Name, Regex Pattern, TokenKind[] Kinds);

    private static readonly Dictionary<string, Language> ByExtension = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Language> ByName = new(StringComparer.OrdinalIgnoreCase);

    static SyntaxHighlighter()
    {
        // ---- Python
        Add("Python", new[] { "py", "pyw", "pyi" }, RegexOptions.None,
            (TokenKind.Comment, @"\#[^\n]*"),
            (TokenKind.String, @"(?:[rRbBfFuU]{1,2})?(?:""""""[\s\S]*?""""""|'''[\s\S]*?'''|""(?:[^""\\\n]|\\.)*""|'(?:[^'\\\n]|\\.)*')"),
            (TokenKind.Function, @"@[\w.]+"),
            (TokenKind.Control, Words("if elif else for while try except finally with return yield break continue pass raise import from as async await match case")),
            (TokenKind.Keyword, Words("def class lambda and or not in is None True False global nonlocal del assert self cls")),
            (TokenKind.Type, Words("int float str bool list dict set tuple bytes object type Exception print len range enumerate zip open super isinstance")),
            (TokenKind.Number, @"\b(?:0[xXoObB][\da-fA-F_]+|\d[\d_]*(?:\.\d*)?(?:[eE][+-]?\d+)?j?)\b"),
            (TokenKind.Function, @"\b[A-Za-z_]\w*(?=\s*\()"));

        // ---- SQL (T-SQL, Spark SQL, PostgreSQL ortak)
        Add("SQL", new[] { "sql", "hql", "ddl", "dml", "psql" }, RegexOptions.IgnoreCase,
            (TokenKind.Comment, @"--[^\n]*|/\*[\s\S]*?\*/"),
            (TokenKind.String, @"N?'(?:[^']|'')*'"),
            (TokenKind.Property, @"\[[^\]\n]+\]|`[^`\n]+`|""[^""\n]+"""),
            (TokenKind.Keyword, Words(
                "select from where and or not in is null as join inner left right full outer cross on group by order having distinct top limit offset " +
                "insert into values update set delete merge using matched then when case else end create alter drop table view index procedure proc function " +
                "trigger schema database if exists begin commit rollback transaction declare exec execute with union all except intersect over partition rows " +
                "range between like ilike asc desc primary key foreign references default constraint unique check returns return go use nolock pivot unpivot " +
                "apply fetch next only truncate grant revoke cast convert try_cast coalesce isnull nullif qualify lateral explode replace temporary temp")),
            (TokenKind.Type, Words(
                "int bigint smallint tinyint bit decimal numeric float real money date datetime datetime2 time timestamp varchar nvarchar char nchar text " +
                "string boolean double binary varbinary uniqueidentifier xml json array map struct")),
            (TokenKind.Number, @"\b\d+(?:\.\d+)?\b"),
            (TokenKind.Function, @"\b[A-Za-z_][\w]*(?=\s*\()"),
            (TokenKind.Attribute, @"@@?\w+"));

        // ---- C benzeri diller
        string cLikeControl = "if else for foreach while do switch case default break continue return goto try catch finally throw yield await";
        Add("C#", new[] { "cs", "csx" }, RegexOptions.None, CLike(cLikeControl,
            "using namespace class struct record interface enum public private protected internal static readonly const sealed abstract virtual override " +
            "async new this base null true false var void get set init required partial event delegate operator is as in out ref params where typeof nameof sizeof lock fixed unsafe",
            "int long short byte bool char string object double float decimal uint ulong ushort sbyte dynamic Task List Dictionary IEnumerable Span"));
        Add("JavaScript", new[] { "js", "mjs", "cjs", "jsx", "ts", "tsx" }, RegexOptions.None, CLike(cLikeControl + " of in",
            "function const let var class extends new this super null undefined true false typeof instanceof delete void import export from as default async " +
            "interface type enum implements public private protected readonly static declare namespace module keyof",
            "string number boolean any unknown never object Array Promise Map Set Record Partial console window document JSON Math Date",
            templateStrings: true));
        Add("Java/Kotlin", new[] { "java", "kt", "kts", "scala", "groovy", "gradle" }, RegexOptions.None, CLike(cLikeControl + " when",
            "class interface enum extends implements public private protected static final abstract new this super null true false import package " +
            "fun val var object companion data sealed override open internal suspend def lazy", "int long short byte boolean char String double float void Unit Int Long Boolean List Map"));
        Add("C/C++", new[] { "c", "h", "cpp", "hpp", "cc", "cxx", "ino" }, RegexOptions.None, CLike(cLikeControl,
            "struct class union enum typedef static const extern inline volatile public private protected virtual template typename namespace using new delete this nullptr true false sizeof auto",
            "int long short char bool void float double unsigned signed size_t uint8_t uint32_t int32_t int64_t std string vector", preprocessor: true));
        Add("Go", new[] { "go" }, RegexOptions.None, CLike(cLikeControl + " range select defer go fallthrough",
            "package import func var const type struct interface map chan nil true false", "int int64 int32 uint string bool byte rune float64 error any"));
        Add("Rust", new[] { "rs" }, RegexOptions.None, CLike(cLikeControl + " loop match in",
            "fn let mut pub use mod crate struct enum impl trait where self Self super const static unsafe as move ref true false dyn async", "i32 i64 u8 u32 u64 usize f64 bool str String Vec Option Result Box"));
        Add("Swift", new[] { "swift" }, RegexOptions.None, CLike(cLikeControl + " guard in repeat",
            "func let var class struct enum protocol extension import public private internal fileprivate static self Self nil true false init deinit override some any async throws",
            "Int Double Float String Bool Array Dictionary Set Void Optional View"));
        Add("CSS", new[] { "css", "scss", "less" }, RegexOptions.None,
            (TokenKind.Comment, @"/\*[\s\S]*?\*/|//[^\n]*"),
            (TokenKind.String, @"""[^""\n]*""|'[^'\n]*'"),
            (TokenKind.Keyword, @"@[\w-]+|!important"),
            (TokenKind.Property, @"[\w-]+(?=\s*:[^:{;]*[;}\n])"),
            (TokenKind.Number, @"#[\da-fA-F]{3,8}\b|-?\b\d+(?:\.\d+)?(?:px|em|rem|%|vh|vw|s|ms|deg|fr)?"),
            (TokenKind.Tag, @"[.#]?[A-Za-z_][\w-]*(?=[^{}\n]*\{)"));

        // ---- İşaretleme dilleri
        Add("HTML", new[] { "html", "htm", "xhtml", "xml", "xaml", "svg", "csproj", "props", "targets", "config", "resw", "resx", "plist", "vue", "razor", "cshtml" }, RegexOptions.None,
            (TokenKind.Comment, @"<!--[\s\S]*?-->"),
            (TokenKind.Keyword, @"<!\w+[^>]*>|<\?[\s\S]*?\?>"),
            (TokenKind.Tag, @"</?[\w:.-]+|/?>"),
            (TokenKind.Attribute, @"(?<=\s)[\w:.@-]+(?=\s*=)"),
            (TokenKind.String, @"""[^""]*""|'[^']*'"));
        Add("JSON", new[] { "json", "jsonc", "ipynb", "geojson", "pbir", "pbism", "pbip", "bim", "webmanifest" }, RegexOptions.None,
            (TokenKind.Comment, @"//[^\n]*|/\*[\s\S]*?\*/"),
            (TokenKind.Property, @"""(?:[^""\\]|\\.)*""(?=\s*:)"),
            (TokenKind.String, @"""(?:[^""\\]|\\.)*"""),
            (TokenKind.Keyword, Words("true false null")),
            (TokenKind.Number, @"-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b"));
        Add("YAML", new[] { "yaml", "yml" }, RegexOptions.Multiline,
            (TokenKind.Comment, @"\#[^\n]*"),
            (TokenKind.Property, @"^[ \t-]*[\w.\-/ ]+?(?=:(?:\s|$))"),
            (TokenKind.String, @"""(?:[^""\\]|\\.)*""|'(?:[^']|'')*'"),
            (TokenKind.Keyword, @"\b(?:true|false|null|yes|no|on|off)\b|![\w!]+|&\w+|\*\w+"),
            (TokenKind.Number, @"\b\d+(?:\.\d+)?\b"));
        Add("INI/TOML", new[] { "ini", "toml", "cfg", "conf", "properties", "env", "editorconfig", "gitconfig" }, RegexOptions.Multiline,
            (TokenKind.Comment, @"^[ \t]*[#;][^\n]*"),
            (TokenKind.Tag, @"^[ \t]*\[[^\]\n]+\]"),
            (TokenKind.Property, @"^[ \t]*[\w.\-]+(?=\s*[=:])"),
            (TokenKind.String, @"""(?:[^""\\]|\\.)*""|'[^'\n]*'"),
            (TokenKind.Keyword, Words("true false")),
            (TokenKind.Number, @"\b\d+(?:\.\d+)?\b"));
        Add("Markdown", new[] { "md", "markdown", "mdx" }, RegexOptions.Multiline,
            (TokenKind.String, @"```[\s\S]*?```|`[^`\n]+`"),
            (TokenKind.Heading, @"^#{1,6}[^\n]*"),
            (TokenKind.Keyword, @"\*\*[^*\n]+\*\*|__[^_\n]+__"),
            (TokenKind.Attribute, @"\[[^\]\n]*\]\([^)\n]*\)"),
            (TokenKind.Control, @"^\s*(?:[-*+]|\d+\.)\s"));

        // ---- Betik dilleri
        Add("PowerShell", new[] { "ps1", "psm1", "psd1" }, RegexOptions.IgnoreCase,
            (TokenKind.Comment, @"<\#[\s\S]*?\#>|\#[^\n]*"),
            (TokenKind.String, @"@""[\s\S]*?""@|@'[\s\S]*?'@|""(?:[^""`]|`.)*""|'(?:[^']|'')*'"),
            (TokenKind.Attribute, @"\$[\w:]+|\$\{[^}]+\}"),
            (TokenKind.Control, Words("if elseif else foreach for while do switch break continue return try catch finally throw trap exit")),
            (TokenKind.Keyword, Words("function param begin process end filter class enum using")),
            (TokenKind.Function, @"\b[A-Za-z]+-[A-Za-z]+\b"),
            (TokenKind.Type, @"\[[\w.]+\]"),
            (TokenKind.Number, @"\b\d+(?:\.\d+)?\b"),
            (TokenKind.Keyword, @"(?<=\s)-[A-Za-z]+\b"));
        Add("Shell", new[] { "sh", "bash", "zsh", "fish", "command" }, RegexOptions.None,
            (TokenKind.Comment, @"\#[^\n]*"),
            (TokenKind.String, @"""(?:[^""\\]|\\.)*""|'[^']*'"),
            (TokenKind.Attribute, @"\$\w+|\$\{[^}]+\}|\$\([^)]*\)"),
            (TokenKind.Control, Words("if then elif else fi for in do done while until case esac return exit break continue")),
            (TokenKind.Keyword, Words("function local export readonly declare source alias sudo echo cd")),
            (TokenKind.Number, @"\b\d+\b"));
        Add("Batch", new[] { "bat", "cmd" }, RegexOptions.IgnoreCase,
            (TokenKind.Comment, @"(?:^|\n)\s*(?:rem\b|::)[^\n]*"),
            (TokenKind.String, @"""[^""\n]*"""),
            (TokenKind.Attribute, @"%[\w~:]+%?|![\w]+!"),
            (TokenKind.Keyword, Words("echo set if else goto call exit for in do not exist defined errorlevel setlocal endlocal pause start")),
            (TokenKind.Tag, @"^:\w+"));
        Add("R", new[] { "r", "rmd" }, RegexOptions.None,
            (TokenKind.Comment, @"\#[^\n]*"),
            (TokenKind.String, @"""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'"),
            (TokenKind.Control, Words("if else for while repeat function return next break in")),
            (TokenKind.Keyword, Words("TRUE FALSE NULL NA Inf NaN library require")),
            (TokenKind.Number, @"\b\d+(?:\.\d+)?L?\b"),
            (TokenKind.Function, @"\b[\w.]+(?=\s*\()"),
            (TokenKind.Keyword, @"<-|->|%>%|\|>"));

        // ---- Power BI: DAX ve Power Query (M)
        Add("DAX", new[] { "dax", "msdax", "tmdl" }, RegexOptions.IgnoreCase,
            (TokenKind.Comment, @"//[^\n]*|--[^\n]*|/\*[\s\S]*?\*/"),
            (TokenKind.String, @"""(?:[^""]|"""")*"""),
            (TokenKind.Property, @"'[^'\n]+'|\[[^\]\n]+\]"),
            (TokenKind.Keyword, Words("var return define evaluate measure column table order by asc desc in not and or true false blank " +
                "model expression partition relationship ref annotation lineageTag formatString summarizeBy dataType sourceColumn isHidden")),
            (TokenKind.Function, @"\b[A-Z][A-Z0-9.]*(?=\s*\()"),
            (TokenKind.Number, @"\b\d+(?:\.\d+)?\b"));
        Add("Power Query", new[] { "m", "pq" }, RegexOptions.None,
            (TokenKind.Comment, @"//[^\n]*|/\*[\s\S]*?\*/"),
            (TokenKind.String, @"#?""(?:[^""]|"""")*"""),
            (TokenKind.Keyword, Words("let in each if then else and or not try otherwise error meta type as is section shared null true false")),
            (TokenKind.Function, @"\b[A-Z][\w]*\.[A-Z][\w.]*"),
            (TokenKind.Type, @"#(?:date|datetime|datetimezone|duration|time|table|binary|shared)\b"),
            (TokenKind.Number, @"\b\d+(?:\.\d+)?\b"));
        Add("Dockerfile", new[] { "dockerfile" }, RegexOptions.IgnoreCase | RegexOptions.Multiline,
            (TokenKind.Comment, @"^\s*\#[^\n]*"),
            (TokenKind.Keyword, @"^\s*(?:FROM|RUN|CMD|LABEL|EXPOSE|ENV|ADD|COPY|ENTRYPOINT|VOLUME|USER|WORKDIR|ARG|ONBUILD|STOPSIGNAL|HEALTHCHECK|SHELL)\b"),
            (TokenKind.String, @"""(?:[^""\\]|\\.)*"""),
            (TokenKind.Attribute, @"\$\w+|\$\{[^}]+\}"));

        // Notebook çekirdek adları
        Alias("python3", "Python");
        Alias("pyspark", "Python");
        Alias("scala", "Java/Kotlin");
        Alias("sql", "SQL");
        Alias("sparksql", "SQL");
        Alias("r", "R");
        Alias("powershell", "PowerShell");
        Alias("csharp", "C#");
        Alias("c#", "C#");
        Alias("javascript", "JavaScript");
        Alias("typescript", "JavaScript");
        Alias("markdown", "Markdown");
        Alias("bash", "Shell");
        Alias("shell", "Shell");
    }

    private static void Alias(string alias, string name)
    {
        if (ByName.TryGetValue(name, out var language)) ByName[alias] = language;
    }

    /// <summary>Dosya adına göre dil; bilinmiyorsa null.</summary>
    public static string? LanguageFor(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)) return "Dockerfile";
        if (name.Equals("Makefile", StringComparison.OrdinalIgnoreCase)) return "Shell";
        var extension = Path.GetExtension(name).TrimStart('.');
        return ByExtension.TryGetValue(extension, out var language) ? language.Name : null;
    }

    public static bool IsSupported(string fileName) => LanguageFor(fileName) is not null;

    /// <summary>Metni belirteçlere ayırır. Dil bulunamazsa tek düz belirteç döner.</summary>
    public static List<Token> Tokenize(string text, string? language)
    {
        var tokens = new List<Token>();
        if (language is null || !ByName.TryGetValue(language, out var lang))
        {
            tokens.Add(new Token(text, TokenKind.Plain));
            return tokens;
        }

        int position = 0;
        try
        {
            foreach (Match match in lang.Pattern.Matches(text))
            {
                if (match.Length == 0) continue;
                if (match.Index > position) tokens.Add(new Token(text[position..match.Index], TokenKind.Plain));
                var kind = TokenKind.Plain;
                for (int g = 0; g < lang.Kinds.Length; g++)
                {
                    if (match.Groups[g + 1].Success)
                    {
                        kind = lang.Kinds[g];
                        break;
                    }
                }
                tokens.Add(new Token(match.Value, kind));
                position = match.Index + match.Length;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Aşırı durumda kalan kısım düz metin
        }
        if (position < text.Length) tokens.Add(new Token(text[position..], TokenKind.Plain));
        return tokens;
    }

    // ---------------------------------------------------------------- tanım yardımcıları

    private static string Words(string words) =>
        @"\b(?:" + string.Join('|', words.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape)) + @")\b";

    private static (TokenKind, string)[] CLike(string control, string keywords, string types, bool templateStrings = false, bool preprocessor = false)
    {
        var rules = new List<(TokenKind, string)>
        {
            (TokenKind.Comment, @"//[^\n]*|/\*[\s\S]*?\*/"),
        };
        if (preprocessor) rules.Add((TokenKind.Keyword, @"^\s*\#\s*\w+"));
        rules.Add((TokenKind.String, templateStrings
            ? @"`(?:[^`\\]|\\.)*`|""(?:[^""\\\n]|\\.)*""|'(?:[^'\\\n]|\\.)*'"
            : @"@""(?:[^""]|"""")*""|\$?""(?:[^""\\\n]|\\.)*""|'(?:[^'\\\n]|\\.){0,8}'"));
        rules.Add((TokenKind.Attribute, @"^\s*\[[A-Z]\w*(?:\([^)\n]*\))?\]|@\w+"));
        rules.Add((TokenKind.Control, Words(control)));
        rules.Add((TokenKind.Keyword, Words(keywords)));
        rules.Add((TokenKind.Type, Words(types)));
        rules.Add((TokenKind.Number, @"\b(?:0[xX][\da-fA-F_]+|\d[\d_]*(?:\.\d+)?(?:[eE][+-]?\d+)?[fFdDmMuUlL]*)\b"));
        rules.Add((TokenKind.Type, @"\b[A-Z][a-z]\w*(?=\s*[<\[]|\s+[a-z_]\w*\s*[=;,)])"));
        rules.Add((TokenKind.Function, @"\b[A-Za-z_]\w*(?=\s*\()"));
        return rules.ToArray();
    }

    private static void Add(string name, string[] extensions, RegexOptions options, params (TokenKind Kind, string Pattern)[] rules)
    {
        var pattern = string.Join('|', rules.Select(r => "(" + RemoveCaptures(r.Pattern) + ")"));
        if (rules.Any(r => r.Pattern.Contains("^")) && !options.HasFlag(RegexOptions.Multiline)) options |= RegexOptions.Multiline;
        Language language;
        try
        {
            language = new Language(name,
                new Regex(pattern, options | RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)),
                rules.Select(r => r.Kind).ToArray());
        }
        catch (ArgumentException ex)
        {
            CrashLog.Write("Sözdizimi kuralı: " + name, ex); // bu dil düz metin olarak gösterilir
            return;
        }
        ByName[name] = language;
        foreach (var extension in extensions) ByExtension.TryAdd(extension, language);
    }

    /// <summary>İç grupları yakalamasız yap: birleşik ifadede grup numaraları kurallara karşılık gelsin.</summary>
    private static string RemoveCaptures(string pattern)
    {
        var builder = new System.Text.StringBuilder(pattern.Length + 8);
        bool inClass = false;
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                builder.Append(c).Append(pattern[++i]);
                continue;
            }
            if (c == '[') inClass = true;
            else if (c == ']') inClass = false;
            builder.Append(c);
            if (c == '(' && !inClass && (i + 1 >= pattern.Length || pattern[i + 1] != '?')) builder.Append("?:");
        }
        return builder.ToString();
    }
}
