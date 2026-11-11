using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace ZeroFileCommander.Services;

/// <summary>
/// Arayüz dili. Kaynak metinler Türkçedir; İngilizce karşılıklar Strings/en.json içindedir
/// (anahtar: Türkçe metin, değer: İngilizce). Dil değişikliği yeniden başlatınca uygulanır.
/// </summary>
public static class Loc
{
    private static readonly Dictionary<string, string> English = Load();

    /// <summary>Etkin dil: "tr" ya da "en". Ayar boşsa Windows'un dili (Türkçe değilse İngilizce).</summary>
    public static string Language { get; } = Resolve();

    public static bool IsEnglish => Language == "en";

    private static string Resolve()
    {
        string setting;
        try
        {
            setting = AppSettings.Current.UiLanguage;
        }
        catch (Exception)
        {
            setting = string.Empty;
        }
        if (setting is "tr" or "en") return setting;
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";
    }

    /// <summary>Açılışta bir kez: Windows'un kendi denetimlerinin (bağlam menüleri vb.) dilini de ayarlar.</summary>
    public static void Initialize()
    {
        var culture = CultureInfo.GetCultureInfo(IsEnglish ? "en-US" : "tr-TR");
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        try
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = culture.Name;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Dil ayarı", ex);
        }
    }

    /// <summary>Metni etkin dile çevirir; karşılığı yoksa olduğu gibi döner.</summary>
    public static string T(string turkish)
    {
        if (!IsEnglish || string.IsNullOrEmpty(turkish)) return turkish;
        return English.TryGetValue(turkish, out var english) ? english : turkish;
    }

    /// <summary>Biçimli metin: <c>Loc.F("{0} dosya kopyalandı", n)</c>.</summary>
    public static string F(string turkishFormat, params object?[] args)
    {
        var format = T(turkishFormat);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.CurrentCulture, turkishFormat, args);
        }
    }

    private static Dictionary<string, string> Load()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ZeroFileCommander.Strings.en.json");
            if (stream is null) return new();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new();
        }
    }

    // ---------------------------------------------------------------- XAML

    private static readonly string[] TextProperties =
    {
        "Text", "Header", "PlaceholderText", "Title", "Label", "Content", "OnContent", "OffContent",
        "Message", "Description", "PrimaryButtonText", "SecondaryButtonText", "CloseButtonText",
    };

    /// <summary>
    /// XAML'de yazılmış metinleri çevirir. InitializeComponent'ten hemen sonra çağrılır;
    /// bu sırada bağlamalar (x:Bind) henüz veri yazmamış olduğundan yalnızca sabit metinler değişir.
    /// </summary>
    public static void Apply(object? root)
    {
        if (!IsEnglish || root is null) return;
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Walk(root, visited);
    }

    private static void Walk(object? node, HashSet<object> visited)
    {
        if (node is null || node is string || !visited.Add(node)) return;

        if (node is Window window)
        {
            window.Title = T(window.Title);
            Walk(window.Content, visited);
            return;
        }

        if (node is DependencyObject dependency)
        {
            TranslateProperties(dependency);
            if (ToolTipService.GetToolTip(dependency) is string tip) ToolTipService.SetToolTip(dependency, T(tip));
            var automationName = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(dependency);
            if (!string.IsNullOrEmpty(automationName))
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dependency, T(automationName));
        }

        switch (node)
        {
            case Panel panel:
                foreach (var child in panel.Children) Walk(child, visited);
                break;
            case Border border:
                Walk(border.Child, visited);
                break;
            case Viewbox viewbox:
                Walk(viewbox.Child, visited);
                break;
            case UserControl user:
                Walk(user.Content, visited);
                break;
            case MenuBar bar:
                foreach (var item in bar.Items) Walk(item, visited);
                break;
            case MenuBarItem barItem:
                foreach (var item in barItem.Items) Walk(item, visited);
                break;
            case MenuFlyoutSubItem sub:
                foreach (var item in sub.Items) Walk(item, visited);
                break;
            case MenuFlyout flyout:
                foreach (var item in flyout.Items) Walk(item, visited);
                break;
            case CommandBar commandBar:
                foreach (var item in commandBar.PrimaryCommands) Walk(item, visited);
                foreach (var item in commandBar.SecondaryCommands) Walk(item, visited);
                Walk(commandBar.Content, visited);
                break;
            case RadioButtons radios:
                TranslateItems(radios.Items, visited);
                break;
            case TabView tabs:
                foreach (var item in tabs.TabItems) Walk(item, visited);
                break;
            case ItemsControl items:
                TranslateItems(items.Items, visited);
                break;
            case ContentControl content:
                Walk(content.Content, visited);
                break;
            case SplitView split:
                Walk(split.Pane, visited);
                Walk(split.Content, visited);
                break;
        }

        if (node is Button { Flyout: { } buttonFlyout }) Walk(buttonFlyout, visited);
        if (node is InfoBar { ActionButton: { } action }) Walk(action, visited);
        if (node is InfoBar { Content: { } infoContent }) Walk(infoContent, visited);
        if (node is FrameworkElement { ContextFlyout: { } contextFlyout }) Walk(contextFlyout, visited);
        if (node is Flyout { Content: { } flyoutContent }) Walk(flyoutContent, visited);
        if (node is Expander { Content: { } expanderContent }) Walk(expanderContent, visited);
        if (node is ScrollViewer { Content: { } scrollContent }) Walk(scrollContent, visited);
    }

    private static void TranslateItems(IList<object> items, HashSet<object> visited)
    {
        for (int i = 0; i < items.Count; i++)
        {
            try
            {
                if (items[i] is string text) items[i] = T(text);
                else Walk(items[i], visited);
            }
            catch (Exception)
            {
                return; // ItemsSource ile bağlı liste: dokunma
            }
        }
    }

    private static void TranslateProperties(DependencyObject element)
    {
        var type = element.GetType();
        foreach (var name in TextProperties)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property is null || !property.CanRead || !property.CanWrite) continue;
            if (property.PropertyType != typeof(string) && property.PropertyType != typeof(object)) continue;
            // Bağlı metinlere dokunma (dosya adları vb. veri).
            if (name == "Text" && element is TextBlock block &&
                block.ReadLocalValue(TextBlock.TextProperty) is Microsoft.UI.Xaml.Data.BindingExpression) continue;
            try
            {
                if (property.GetValue(element) is string value && value.Length > 0)
                {
                    var translated = T(value);
                    if (!ReferenceEquals(translated, value) && translated != value) property.SetValue(element, translated);
                }
            }
            catch (Exception)
            {
                // salt okunur ya da desteklenmeyen özellik
            }
        }
    }
}
