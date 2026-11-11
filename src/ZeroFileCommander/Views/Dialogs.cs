using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ZeroFileCommander.Models;
using ZeroFileCommander.Services;

namespace ZeroFileCommander.Views;

/// <summary>Kod ile kurulan küçük diyaloglar.</summary>
public static class Dialogs
{
    public static ContentDialog Create(XamlRoot root, ElementTheme theme, string title, object content,
        string? primary = null, string? close = null, ContentDialogButton defaultButton = ContentDialogButton.Close)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Title = title,
            Content = content,
            CloseButtonText = close ?? Loc.T("Kapat"),
            DefaultButton = defaultButton,
        };
        if (primary is not null) dialog.PrimaryButtonText = primary;
        Overlay.Track(dialog);
        return dialog;
    }

    public static async Task MessageAsync(XamlRoot root, ElementTheme theme, string title, string message) =>
        await Create(root, theme, title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, close: Loc.T("Tamam")).ShowAsync();

    public static async Task<bool> ConfirmAsync(XamlRoot root, ElementTheme theme, string title, string message, string confirm)
    {
        var dialog = Create(root, theme, title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, confirm, Loc.T("Vazgeç"));
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Geri alınamayan işlemler için kırmızı uyarılı onay; varsayılan düğme "Vazgeç".</summary>
    public static async Task<bool> ConfirmDangerAsync(XamlRoot root, ElementTheme theme, string title, string message, string confirm)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, MaxWidth = 440 };
        panel.Children.Add(new FontIcon
        {
            Glyph = "\uE7BA",
            FontSize = 28,
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
        });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 });

        var dialog = Create(root, theme, title, panel, confirm, Loc.T("Vazgeç"), ContentDialogButton.Close);
        var danger = new Style(typeof(Button));
        danger.Setters.Add(new Setter(Control.BackgroundProperty, Application.Current.Resources["SystemFillColorCriticalBrush"]));
        danger.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Microsoft.UI.Colors.White)));
        dialog.PrimaryButtonStyle = danger;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Tek satırlık metin ister; <paramref name="selectStem"/> uzantıyı seçim dışında bırakır.</summary>
    public static async Task<string?> InputAsync(XamlRoot root, ElementTheme theme, string title, string initial, string confirm, bool selectStem = false)
    {
        var box = new TextBox { Text = initial, Width = 380 };
        box.Loaded += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            int length = selectStem && Path.HasExtension(initial) ? Path.GetFileNameWithoutExtension(initial).Length : initial.Length;
            box.Select(0, length);
        };
        var dialog = Create(root, theme, title, box, confirm, Loc.T("Vazgeç"), ContentDialogButton.Primary);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var text = box.Text.Trim();
        return text.Length == 0 ? null : text;
    }

    public static async Task PropertiesAsync(XamlRoot root, ElementTheme theme, FileItem item)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 8, Width = 420 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int row = 0;
        TextBlock AddRow(string label, string value)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
            var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            Grid.SetRow(l, row);
            Grid.SetRow(v, row);
            Grid.SetColumn(v, 1);
            grid.Children.Add(l);
            grid.Children.Add(v);
            row++;
            return v;
        }

        FileSystemInfo info = item.IsDirectory ? new DirectoryInfo(item.FullPath) : new FileInfo(item.FullPath);
        AddRow("Ad", item.Name);
        AddRow(Loc.T("Tür"), item.IsDirectory ? Loc.T("Klasör") : $"{FileKinds.Label(item.Kind)} (.{item.Extension.ToLowerInvariant()})");
        AddRow(Loc.T("Konum"), Path.GetDirectoryName(item.FullPath) ?? item.FullPath);
        var sizeText = AddRow(Loc.T("Boyut"), item.IsDirectory ? Loc.T("Hesaplanıyor…") : Loc.F("{0} ({1:N0} bayt)", item.SizeText, item.Size));
        TextBlock? countText = item.IsDirectory ? AddRow(Loc.T("İçerik"), Loc.T("Hesaplanıyor…")) : null;
        try
        {
            AddRow(Loc.T("Oluşturulma"), info.CreationTime.ToString("dd.MM.yyyy HH:mm:ss"));
            AddRow(Loc.T("Değiştirilme"), info.LastWriteTime.ToString("dd.MM.yyyy HH:mm:ss"));
            AddRow(Loc.T("Erişim"), info.LastAccessTime.ToString("dd.MM.yyyy HH:mm:ss"));
            AddRow(Loc.T("Öznitelikler"), Attributes(info.Attributes));
        }
        catch
        {
            // erişim yoksa temel bilgiler yeter
        }

        if (item.Kind == FileKind.Image)
        {
            var dims = AddRow(Loc.T("Boyutlar"), "…");
            var dq = dims.DispatcherQueue;
            _ = Task.Run(async () =>
            {
                try
                {
                    var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(item.FullPath);
                    var props = await file.Properties.GetImagePropertiesAsync();
                    dq.TryEnqueue(() => dims.Text = Loc.F("{0} × {1} piksel", props.Width, props.Height));
                }
                catch
                {
                    dq.TryEnqueue(() => dims.Text = "—");
                }
            });
        }

        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(grid);
        var windowsProps = new HyperlinkButton { Content = Loc.T("Windows özellikler penceresini aç"), Padding = new Thickness(0) };
        windowsProps.Click += (_, _) => ShellActions.ShowWindowsProperties(item.FullPath);
        panel.Children.Add(windowsProps);

        using var cts = new CancellationTokenSource();
        if (item.IsDirectory)
        {
            _ = FillFolderAsync(item.FullPath, sizeText, countText!, cts.Token);
        }

        await Create(root, theme, Loc.T("Özellikler"), panel).ShowAsync();
        cts.Cancel();
    }

    private static async Task FillFolderAsync(string path, TextBlock size, TextBlock count, CancellationToken token)
    {
        try
        {
            var (folders, files, capped) = await FilePreview.CountAsync(path);
            count.Text = capped ? Loc.T("10.000+ öğe") : Loc.F("{0} klasör, {1} dosya", folders, files);
            long bytes = await FilePreview.FolderSizeAsync(path, token);
            size.Text = Loc.F("{0} ({1:N0} bayt)", FileItem.FormatSize(bytes), bytes);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            size.Text = Loc.T("Hesaplanamadı");
        }
    }

    private static string Attributes(FileAttributes a)
    {
        var parts = new List<string>();
        if (a.HasFlag(FileAttributes.ReadOnly)) parts.Add(Loc.T("Salt okunur"));
        if (a.HasFlag(FileAttributes.Hidden)) parts.Add(Loc.T("Gizli"));
        if (a.HasFlag(FileAttributes.System)) parts.Add(Loc.T("Sistem"));
        if (a.HasFlag(FileAttributes.Archive)) parts.Add(Loc.T("Arşiv"));
        if (a.HasFlag(FileAttributes.Compressed)) parts.Add(Loc.T("Sıkıştırılmış"));
        if (a.HasFlag(FileAttributes.Encrypted)) parts.Add(Loc.T("Şifreli"));
        if (a.HasFlag(FileAttributes.ReparsePoint)) parts.Add(Loc.T("Bağlantı"));
        return parts.Count == 0 ? "—" : string.Join(", ", parts);
    }

    public static async Task AboutAsync(XamlRoot root, ElementTheme theme)
    {
        var version = Windows.ApplicationModel.Package.Current.Id.Version;
        var panel = new StackPanel { Spacing = 14, Width = 400 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        header.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("ms-appx:///Assets/Square150x150Logo.png")),
            Width = 64,
            Height = 64,
        });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        titles.Children.Add(new TextBlock { Text = "Zero File Commander", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        titles.Children.Add(new TextBlock
        {
            Text = Loc.F("Sürüm {0}.{1}.{2}", version.Major, version.Minor, version.Build),
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        header.Children.Add(titles);
        panel.Children.Add(header);

        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("Klasik çift panelli dosya yöneticilerinin gücü, modern Windows 11 tadında. Sıfır gecikmeyle dosya yönetimi."),
            TextWrapping = TextWrapping.Wrap,
        });

        var links = new StackPanel { Spacing = 0 };
        links.Children.Add(new HyperlinkButton { Content = Loc.T("GitHub'da kaynak kod"), NavigateUri = new Uri("https://github.com/aliapaydin/zero-file-commander"), Padding = new Thickness(0, 4, 0, 4) });
        links.Children.Add(new HyperlinkButton { Content = Loc.T("Hata bildir / özellik iste"), NavigateUri = new Uri("https://github.com/aliapaydin/zero-file-commander/issues"), Padding = new Thickness(0, 4, 0, 4) });
        panel.Children.Add(links);

        var license = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        license.Inlines.Add(new Run { Text = Loc.T("MIT lisansı ile açık kaynak. © 2026 Ali Apaydın") });
        panel.Children.Add(license);

        await Create(root, theme, Loc.T("Hakkında"), panel).ShowAsync();
    }

    public static async Task ShortcutsAsync(XamlRoot root, ElementTheme theme)
    {
        (string Key, string Action)[] rows =
        [
            ("Tab", Loc.T("Paneller arasında geç")),
            (Loc.T("Enter / çift tık"), Loc.T("Aç")),
            ("Backspace", Loc.T("Üst klasör")),
            ("Space", Loc.T("Önizle / hızlı çal")),
            ("F2", Loc.T("Yeniden adlandır")),
            ("F3 / F4", Loc.T("Görüntüle / düzenle")),
            ("F5 / F6", Loc.T("Karşı panele kopyala / taşı")),
            ("F7", Loc.T("Yeni klasör")),
            ("F8 / Del", Loc.T("Geri Dönüşüm Kutusu'na gönder")),
            ("Shift+Del", Loc.T("Kalıcı olarak sil (onay ister)")),
            ("Ctrl+C / X / V", Loc.T("Kopyala / kes / yapıştır")),
            ("Ctrl+Shift+C", Loc.T("Yolu kopyala")),
            ("Alt+Enter", Loc.T("Özellikler")),
            ("Ctrl+R", Loc.T("Yenile")),
            ("Ctrl+Q", Loc.T("Önizleme panelini aç/kapat")),
            ("Ctrl+Shift+E", Loc.T("Klasör ağacını aç/kapat")),
            ("Ctrl+J", Loc.T("İndirmeler")),
            ("Ctrl+K", Loc.T("Komut paleti")),
            ("Ctrl+N", Loc.T("Yeni pencere")),
            ("Ctrl+Shift+→", Loc.T("Seçimi diğer pencereye kopyala")),
            ("Ctrl+Shift+S", Loc.T("Raf (geçici toplama alanı)")),
            ("Ctrl+T / Ctrl+W", Loc.T("Yeni sekme / sekmeyi kapat")),
            ("Ctrl+Tab", Loc.T("Sonraki sekme")),
            ("Ctrl+L", Loc.T("Yolu yazarak düzenle")),
            ("Ctrl+M", Loc.T("Toplu yeniden adlandır")),
            ("Alt+F7", Loc.T("Ara")),
            ("Insert", Loc.T("İşaretle ve aşağı in")),
            ("Num + / − / *", Loc.T("Desenle seç / kaldır / tersine çevir")),
            (Loc.T("Harf yaz"), Loc.T("Listeyi filtrele (Esc temizler)")),
            ("Enter (ZIP)", Loc.T("Arşivin içine gir")),
            ("Ctrl+,", Loc.T("Ayarlar")),
            (Loc.T("Sürükle"), Loc.T("Taşı (Ctrl basılıyken kopyala)")),
        ];

        var grid = new Grid { ColumnSpacing = 20, RowSpacing = 6, Width = 420 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var key = new TextBlock { Text = rows[i].Key, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
            var action = new TextBlock { Text = rows[i].Action };
            Grid.SetRow(key, i);
            Grid.SetRow(action, i);
            Grid.SetColumn(action, 1);
            grid.Children.Add(key);
            grid.Children.Add(action);
        }
        await Create(root, theme, Loc.T("Klavye kısayolları"), new ScrollViewer { Content = grid, MaxHeight = 480 }).ShowAsync();
    }
}
