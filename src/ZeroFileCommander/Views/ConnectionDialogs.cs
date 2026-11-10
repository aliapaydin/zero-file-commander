using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ZeroFileCommander.Services.Connections;

namespace ZeroFileCommander.Views;

/// <summary>Bağlantı ekleme/düzenleme, yönetme, parola ve sunucu anahtarı diyalogları.</summary>
public static class ConnectionDialogs
{
    public sealed record EditResult(SavedConnection Connection, string? Password, bool PasswordChanged, bool SavePassword);

    private static readonly ConnectionKind[] Kinds = { ConnectionKind.Sftp, ConnectionKind.Ftps, ConnectionKind.Ftp, ConnectionKind.Smb };

    /// <summary>Yeni bağlantı ya da mevcut bağlantıyı düzenleme. Vazgeçilirse null.</summary>
    public static async Task<EditResult?> EditAsync(XamlRoot root, ElementTheme theme, IntPtr hwnd, SavedConnection? existing, string? error = null)
    {
        var connection = existing?.Clone() ?? new SavedConnection();
        bool isNew = existing is null;

        var name = new TextBox { Header = "Ad", PlaceholderText = "ör. NAS, Web sunucusu", Text = connection.Name };
        var kind = new ComboBox
        {
            Header = "Tür",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "SFTP (SSH)", "FTPS (TLS ile FTP)", "FTP (şifrelenmemiş)", "Ağ paylaşımı / NAS (SMB)" },
            SelectedIndex = Array.IndexOf(Kinds, connection.Kind),
        };
        var host = new TextBox { Header = "Sunucu", PlaceholderText = "sunucu.ornek.com ya da 192.168.1.10", Text = connection.Host };
        var port = new NumberBox
        {
            Header = "Bağlantı noktası",
            Minimum = 0,
            Maximum = 65535,
            Value = connection.Port > 0 ? connection.Port : double.NaN, // boş = varsayılan (SFTP 22, FTP 21)
            PlaceholderText = "22 / 21",
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            Width = 130,
        };
        var user = new TextBox { Header = "Kullanıcı adı", Text = connection.User };
        bool hasStored = !isNew && ConnectionStore.GetPassword(connection) is not null;
        var password = new PasswordBox
        {
            Header = "Parola",
            PlaceholderText = hasStored ? "Kayıtlı · değiştirmek için yazın" : string.Empty,
        };
        var savePassword = new CheckBox
        {
            Content = "Parolayı Windows Kimlik Bilgisi Kasası'nda sakla",
            IsChecked = isNew || ConnectionStore.IsPasswordPersisted(connection),
        };
        var path = new TextBox { Header = "Başlangıç klasörü", Text = connection.Path };
        var keyFile = new TextBox { Header = "Özel anahtar dosyası (isteğe bağlı)", Text = connection.KeyFile ?? string.Empty, PlaceholderText = "id_ed25519" };
        var browse = new Button { Content = "Gözat…", VerticalAlignment = VerticalAlignment.Bottom };
        browse.Click += async (_, _) =>
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is not null) keyFile.Text = file.Path;
        };
        var keyRow = new Grid { ColumnSpacing = 8 };
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        keyRow.Children.Add(keyFile);
        Grid.SetColumn(browse, 1);
        keyRow.Children.Add(browse);

        var hint = new InfoBar { IsOpen = false, IsClosable = false };
        var errorBar = new InfoBar { IsOpen = error is not null, IsClosable = false, Severity = InfoBarSeverity.Error, Message = error ?? string.Empty };

        var hostRow = new Grid { ColumnSpacing = 8 };
        hostRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hostRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hostRow.Children.Add(host);
        Grid.SetColumn(port, 1);
        hostRow.Children.Add(port);

        void UpdateKind()
        {
            var selected = kind.SelectedIndex >= 0 ? Kinds[kind.SelectedIndex] : ConnectionKind.Sftp;
            bool smb = selected == ConnectionKind.Smb;
            port.Visibility = smb ? Visibility.Collapsed : Visibility.Visible;
            keyRow.Visibility = selected == ConnectionKind.Sftp ? Visibility.Visible : Visibility.Collapsed;
            path.Header = smb ? "Paylaşım ve klasör" : "Başlangıç klasörü";
            path.PlaceholderText = smb ? @"Paylasim\Klasor" : "boş bırakılırsa ev klasörü";
            host.PlaceholderText = smb ? "NAS-ADI ya da 192.168.1.20" : "sunucu.ornek.com ya da 192.168.1.10";
            user.PlaceholderText = smb ? @"boşsa Windows hesabınız · ALAN\kullanici" : selected == ConnectionKind.Ftp ? "boşsa anonymous" : string.Empty;
            password.Header = selected == ConnectionKind.Sftp ? "Parola (ya da anahtar parolası)" : "Parola";
            (hint.IsOpen, hint.Severity, hint.Message) = selected switch
            {
                ConnectionKind.Ftp => (true, InfoBarSeverity.Warning, "FTP, parolanızı ve dosyaları şifrelemeden gönderir. Sunucu destekliyorsa FTPS ya da SFTP seçin."),
                ConnectionKind.Smb => (true, InfoBarSeverity.Informational, "Paylaşım Windows'un kendi ağ bağlantısıyla açılır; bağlandıktan sonra tüm işlemler (önizleme, kuyruk, Git) normal klasördeki gibi çalışır."),
                _ => (false, InfoBarSeverity.Informational, string.Empty),
            };
        }
        kind.SelectionChanged += (_, _) => UpdateKind();
        UpdateKind();

        var form = new StackPanel { Spacing = 10, Width = 440 };
        form.Children.Add(errorBar);
        form.Children.Add(name);
        form.Children.Add(kind);
        form.Children.Add(hostRow);
        form.Children.Add(user);
        form.Children.Add(password);
        form.Children.Add(savePassword);
        form.Children.Add(path);
        form.Children.Add(keyRow);
        form.Children.Add(hint);
        var scroller = new ScrollViewer { Content = form, MaxHeight = 560, Padding = new Thickness(0, 0, 12, 0) };

        var dialog = Dialogs.Create(root, theme, isNew ? "Yeni bağlantı" : "Bağlantıyı düzenle", scroller,
            isNew ? "Kaydet ve bağlan" : "Kaydet", "Vazgeç", ContentDialogButton.Primary);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (host.Text.Trim().Length == 0)
            {
                errorBar.Message = "Sunucu adı gerekli.";
                errorBar.IsOpen = true;
                args.Cancel = true;
            }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        connection.Kind = Kinds[Math.Max(0, kind.SelectedIndex)];
        connection.Host = host.Text.Trim().TrimStart('\\').TrimEnd('/', '\\');
        connection.Port = double.IsNaN(port.Value) || port.Value <= 0 ? 0 : (int)port.Value;
        connection.User = user.Text.Trim();
        connection.Path = path.Text.Trim();
        connection.KeyFile = connection.Kind == ConnectionKind.Sftp && keyFile.Text.Trim().Length > 0 ? keyFile.Text.Trim() : null;
        connection.Name = name.Text.Trim().Length > 0 ? name.Text.Trim()
            : connection.Kind == ConnectionKind.Smb ? connection.UncPath : connection.Host;
        // Sunucu değiştiyse eski anahtar onayı geçersiz.
        if (existing is not null && (existing.Host != connection.Host || existing.EffectivePort != connection.EffectivePort))
            connection.HostKey = null;

        bool changed = password.Password.Length > 0;
        return new EditResult(connection, changed ? password.Password : ConnectionStore.GetPassword(connection), changed, savePassword.IsChecked == true);
    }

    /// <summary>Kayıtlı bağlantılar: aç, düzenle, sil. Seçilen işlem ve bağlantıyı döndürür.</summary>
    public enum ManageAction { None, Open, Edit, Delete, New }

    public static async Task<(ManageAction Action, SavedConnection? Connection)> ManageAsync(XamlRoot root, ElementTheme theme)
    {
        var result = (ManageAction.None, (SavedConnection?)null);
        var list = new StackPanel { Spacing = 6, Width = 480 };
        ContentDialog? dialog = null;

        void Choose(ManageAction action, SavedConnection? connection)
        {
            result = (action, connection);
            dialog?.Hide();
        }

        if (ConnectionStore.All.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = "Henüz kayıtlı bağlantı yok. NAS, SFTP ya da FTP sunucusu ekleyebilirsiniz.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
        }
        foreach (var connection in ConnectionStore.All.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(10, 8, 6, 8), CornerRadius = new CornerRadius(6) };
            row.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new FontIcon { Glyph = connection.Glyph, FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = connection.Name, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock
            {
                Text = connection.Kind == ConnectionKind.Smb ? connection.UncPath : $"{connection.KindLabel} · {connection.RootUri}",
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            buttons.Children.Add(IconButton("\uE8A7", "Bağlan", () => Choose(ManageAction.Open, connection)));
            buttons.Children.Add(IconButton("\uE70F", "Düzenle", () => Choose(ManageAction.Edit, connection)));
            buttons.Children.Add(IconButton("\uE74D", "Sil", () => Choose(ManageAction.Delete, connection)));
            Grid.SetColumn(buttons, 2);
            row.Children.Add(buttons);
            list.Children.Add(row);
        }

        dialog = Dialogs.Create(root, theme, "Bağlantılar", new ScrollViewer { Content = list, MaxHeight = 480 }, "Yeni bağlantı…", "Kapat");
        var shown = await dialog.ShowAsync();
        if (shown == ContentDialogResult.Primary) return (ManageAction.New, null);
        return result;
    }

    private static Button IconButton(string glyph, string tip, Action click)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            Padding = new Thickness(8),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(button, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Oturum açma başarısız olunca parolayı yeniden ister.</summary>
    public static async Task<(string Password, bool Save)?> PasswordAsync(XamlRoot root, ElementTheme theme, SavedConnection connection, string message)
    {
        var panel = new StackPanel { Spacing = 10, Width = 380 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var box = new PasswordBox
        {
            Header = connection.User.Length > 0 ? $"{connection.User} için parola" : "Parola",
        };
        box.Loaded += (_, _) => box.Focus(FocusState.Programmatic);
        var save = new CheckBox { Content = "Parolayı sakla", IsChecked = ConnectionStore.IsPasswordPersisted(connection) || ConnectionStore.GetPassword(connection) is null };
        panel.Children.Add(box);
        panel.Children.Add(save);
        var dialog = Dialogs.Create(root, theme, connection.Name, panel, "Bağlan", "Vazgeç", ContentDialogButton.Primary);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        return (box.Password, save.IsChecked == true);
    }

    /// <summary>İlk bağlantıda sunucu anahtarını kullanıcıya onaylatır; değişmişse yalnızca uyarır.</summary>
    public static async Task<bool> TrustHostKeyAsync(XamlRoot root, ElementTheme theme, SavedConnection connection, RemoteHostKeyException key)
    {
        if (key.IsMismatch)
        {
            await Dialogs.ConfirmDangerAsync(root, theme, "Sunucu anahtarı değişmiş",
                $"{connection.Host} sunucusunun kimlik anahtarı daha önce onayladığınızdan farklı.\n\n" +
                $"Yeni parmak izi: {key.Fingerprint}\nKayıtlı: {connection.HostKey}\n\n" +
                "Sunucu yeniden kurulduysa bu normaldir; değilse biri araya giriyor olabilir. Güvenlik için bağlanılmadı. " +
                "Değişikliği sunucu yöneticisiyle doğruladıysanız bağlantıyı düzenleyip sunucu adını yeniden kaydedin.",
                "Anladım");
            return false;
        }

        var panel = new StackPanel { Spacing = 10, Width = 440 };
        panel.Children.Add(new TextBlock
        {
            Text = $"{connection.Host} sunucusuna ilk kez bağlanıyorsunuz. Parmak izinin sunucunuzunkiyle aynı olduğunu doğrulayın:",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{key.Algorithm}\n{key.Fingerprint}",
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Onaylarsanız anahtar kaydedilir; ileride değişirse uyarılırsınız.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        var dialog = Dialogs.Create(root, theme, "Sunucu kimliğini doğrulayın", panel, "Güven ve bağlan", "Vazgeç", ContentDialogButton.Close);
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
