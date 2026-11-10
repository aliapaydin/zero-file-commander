using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Windows.UI.Core;
using ZeroFileCommander.Controls;
using ZeroFileCommander.Models;
using ZeroFileCommander.Services;
using ZeroFileCommander.Views;

namespace ZeroFileCommander;

public sealed partial class MainWindow : Window, IPaneHost
{
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly DispatcherQueueTimer _previewTimer;
    private readonly TaskbarProgress _taskbar;
    private readonly TrayIcon _tray;
    private readonly WindowPlacement _placement;
    private FilePane _active;
    private DownloadsWindow? _downloadsWindow;
    private bool _canPaste;
    private bool _exiting;
    private bool _trayHintShown;
    private string? _lastCapturedUrl;
    private DateTime _drivesRefreshed = DateTime.MinValue;
    private readonly List<(string Root, Button Button)> _driveButtons = new();

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        AppWindow.SetIcon(iconPath);
        _placement = new WindowPlacement(AppWindow, DispatcherQueue,
            () => _settings.MainWindowBounds, value => _settings.MainWindowBounds = value);
        _placement.Restore(new Windows.Graphics.SizeInt32(1480, 900));

        // Paneller
        _active = LeftPane;
        foreach (var pane in new[] { LeftPane, RightPane })
        {
            pane.Host = this;
            pane.Activated += (s, _) => SetActive((FilePane)s!);
            pane.SelectionChangedEx += Pane_SelectionChanged;
        }
        Preview.CloseRequested += (_, _) => _settings.ShowPreviewPane = false;
        Preview.HostWindow = WinRT.Interop.WindowNative.GetWindowHandle(this);

        // Ayırıcılar
        TreeSplitter.Dragged += (_, dx) => TreeColumn.Width = new GridLength(Math.Clamp(TreeColumn.Width.Value + dx, 160, 600));
        TreeSplitter.DragCompleted += (_, _) => _settings.TreeWidth = TreeColumn.Width.Value;
        TreeSplitter.ResetRequested += (_, _) => TreeColumn.Width = new GridLength(250);
        PreviewSplitter.Dragged += (_, dx) => PreviewColumn.Width = new GridLength(Math.Clamp(PreviewColumn.Width.Value - dx, 240, 1200));
        PreviewSplitter.DragCompleted += (_, _) => _settings.PreviewWidth = PreviewColumn.Width.Value;
        PreviewSplitter.ResetRequested += (_, _) => PreviewColumn.Width = new GridLength(360);
        MiddleSplitter.Dragged += (_, dx) => ResizePanes(dx);
        MiddleSplitter.DragCompleted += (_, _) => _settings.LeftRatio = CurrentLeftRatio();
        MiddleSplitter.ResetRequested += (_, _) => SetLeftRatio(0.5);

        // "Yeni" menüleri
        NewMenu.Populate(RibbonNewMenu.Items, entry => _ = CreateNewAsync(_active, entry));
        NewMenu.Populate(MenuNew.Items, entry => _ = CreateNewAsync(_active, entry));
        Overlay.Track(RibbonNewMenu);

        _previewTimer = DispatcherQueue.CreateTimer();
        _previewTimer.Interval = TimeSpan.FromMilliseconds(160);
        _previewTimer.IsRepeating = false;
        _previewTimer.Tick += async (_, _) => await UpdatePreviewAsync(autoPlay: false);

        // Pano
        _canPaste = ClipboardService.HasFiles();
        Clipboard.ContentChanged += Clipboard_ContentChanged;

        // Görev çubuğu, tepsi, indirmeler
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _taskbar = new TaskbarProgress(hwnd);
        _tray = new TrayIcon(hwnd, iconPath, "Zero File Commander") { MenuProvider = BuildTrayMenu };
        _tray.OpenRequested += (_, _) => ShowFromTray();
        if (_settings.RunInBackground) _tray.Show();

        DownloadManager.Current.Attach(DispatcherQueue);
        DownloadManager.Current.ProgressChanged += (_, _) => UpdateDownloadIndicators();
        DownloadManager.Current.Completed += Download_Completed;

        AppWindow.Closing += AppWindow_Closing;
        AppWindow.Changed += AppWindow_Changed;
        _settings.Changed += Settings_Changed;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        LeftPane.SettingsKey = "Left";
        RightPane.SettingsKey = "Right";
        LeftPane.Restore(Directory.Exists(_settings.LeftPath) ? _settings.LeftPath : home);
        RightPane.Restore(Directory.Exists(_settings.RightPath) ? _settings.RightPath : string.Empty);
        SetActive(LeftPane);
        LeftPane.FocusList();

        SetLeftRatio(_settings.LeftRatio);
        ApplyAllSettings();
        UpdateRibbon();

        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated) _ = RefreshDrivesAsync();
        };
        _ = RefreshDrivesAsync(force: true);
    }

    private FilePane Other => _active == LeftPane ? RightPane : LeftPane;

    private IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    private ElementTheme DialogTheme => RootGrid.ActualTheme;

    public bool CanPaste => _canPaste;

    // ================================================================ Panel yönetimi

    private void SetActive(FilePane pane)
    {
        HighlightActiveDrive(pane);
        if (_active != pane)
        {
            _active = pane;
            Pane_SelectionChanged(pane, EventArgs.Empty);
        }
        LeftPane.SetActive(pane == LeftPane);
        RightPane.SetActive(pane == RightPane);
    }

    private void Pane_SelectionChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _active)) return;
        HighlightActiveDrive(_active);
        UpdateRibbon();
        if (_settings.ShowPreviewPane)
        {
            _previewTimer.Stop();
            _previewTimer.Start();
        }
    }

    private int _previewRequest;

    private async Task UpdatePreviewAsync(bool autoPlay)
    {
        if (!_settings.ShowPreviewPane) return;
        var item = _active.FocusedItem;
        int request = ++_previewRequest;
        var resolved = item is { IsEntry: true } ? await ResolvePreviewItemAsync(item) : null;
        if (request != _previewRequest) return;
        await Preview.ShowAsync(resolved, autoPlay);
    }

    /// <summary>Arşiv içindeki dosyayı önizleme için geçici klasöre çıkarır.</summary>
    private static async Task<FileItem?> ResolvePreviewItemAsync(FileItem item)
    {
        if (!item.IsInArchive || item.IsDirectory) return item;
        if ((item.Size ?? 0) > 64L * 1024 * 1024) return null;
        try
        {
            var temp = await ArchiveService.ExtractToTempAsync(item);
            return new FileItem { Name = item.Name, FullPath = temp, Size = item.Size, Modified = item.Modified };
        }
        catch
        {
            return null;
        }
    }

    private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool inText = FocusManager.GetFocusedElement(Content.XamlRoot) is TextBox;

        // Ctrl+Tab / Ctrl+Shift+Tab: aktif paneldeki sekmeler arasında geç.
        if (e.Key == VirtualKey.Tab && IsDown(VirtualKey.Control))
        {
            _active.NextTab(IsDown(VirtualKey.Shift) ? -1 : 1);
            _active.FocusList();
            e.Handled = true;
            return;
        }

        // Tab ile paneller arasında geç.
        if (e.Key == VirtualKey.Tab && !inText)
        {
            SetActive(Other);
            _active.FocusList();
            e.Handled = true;
            return;
        }

        // Ctrl+, → Ayarlar (VirtualKey listesinde virgül yok: 0xBC)
        if ((int)e.Key == 0xBC && IsDown(VirtualKey.Control))
        {
            _ = ShowSettingsAsync();
            e.Handled = true;
        }
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    // ================================================================ Komutlar

    private void Accel_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        bool inText = FocusManager.GetFocusedElement(Content.XamlRoot) is TextBox;
        bool ctrl = sender.Modifiers.HasFlag(VirtualKeyModifiers.Control);
        bool shift = sender.Modifiers.HasFlag(VirtualKeyModifiers.Shift);

        string? tag = sender.Key switch
        {
            VirtualKey.F1 => "app:shortcuts",
            VirtualKey.F2 => nameof(PaneCommand.Rename),
            VirtualKey.F3 => nameof(PaneCommand.Preview),
            VirtualKey.F4 => nameof(PaneCommand.Edit),
            VirtualKey.F5 => nameof(PaneCommand.CopyToOther),
            VirtualKey.F6 => nameof(PaneCommand.MoveToOther),
            VirtualKey.F7 => nameof(PaneCommand.NewFolder),
            VirtualKey.F8 => nameof(PaneCommand.Delete),
            VirtualKey.Delete when !inText => nameof(PaneCommand.Delete),
            VirtualKey.C when ctrl && shift => nameof(PaneCommand.CopyPath),
            VirtualKey.C when ctrl && !inText => nameof(PaneCommand.Copy),
            VirtualKey.X when ctrl && !inText => nameof(PaneCommand.Cut),
            VirtualKey.V when ctrl && !inText => nameof(PaneCommand.Paste),
            VirtualKey.R when ctrl => nameof(PaneCommand.Refresh),
            VirtualKey.Enter => nameof(PaneCommand.Properties),
            VirtualKey.Q when ctrl => "app:preview",
            VirtualKey.E when ctrl && shift => "app:tree",
            VirtualKey.J when ctrl => "app:downloads",
            VirtualKey.T when ctrl => "app:newtab",
            VirtualKey.W when ctrl => "app:closetab",
            VirtualKey.L when ctrl => "app:editpath",
            _ => null,
        };
        if (tag is null) return;
        args.Handled = true;
        RunTag(tag);
    }

    private void Command_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag) RunTag(tag);
    }

    private void RunTag(string tag)
    {
        if (Enum.TryParse<PaneCommand>(tag, out var command))
            Execute(command, _active);
        else
            _ = RunAppCommandAsync(tag);
    }

    public void Execute(PaneCommand command, FilePane pane)
    {
        if (pane != _active) SetActive(pane);
        _ = ExecuteAsync(command, pane);
    }

    private async Task ExecuteAsync(PaneCommand command, FilePane pane)
    {
        var selection = pane.SelectedItems;
        var focus = pane.FocusedItem is { IsEntry: true } f ? f : null;
        var target = focus ?? selection.FirstOrDefault();

        if (pane.IsInArchive && command is PaneCommand.Rename or PaneCommand.NewFolder or PaneCommand.Cut
                or PaneCommand.OpenTerminal or PaneCommand.RunAsAdmin or PaneCommand.MoveToOther or PaneCommand.Compress)
        {
            await Dialogs.MessageAsync(Content.XamlRoot, DialogTheme, "Arşiv içinde yapılamıyor",
                "Bu işlem ZIP'in içinde desteklenmiyor. Dosyaları önce karşı panele çıkarın (F5).");
            return;
        }

        // Arşivdeki dosyalarla dış uygulamalar için önce geçici bir kopya çıkar.
        async Task<string?> RealPathAsync(FileItem item)
        {
            if (!item.IsInArchive) return item.FullPath;
            return item.IsDirectory ? null : await ArchiveService.ExtractToTempAsync(item);
        }

        try
        {
            switch (command)
            {
                case PaneCommand.Open:
                    if (pane.FocusedItem is { } open) pane.Open(open);
                    break;
                case PaneCommand.OpenWith when target is { IsDirectory: false }:
                    if (await RealPathAsync(target) is { } openWithPath) ShellActions.OpenWith(openWithPath);
                    break;
                case PaneCommand.Preview when target is not null:
                    if (target.IsDirectory) { pane.Open(target); break; }
                    await ShowPreviewPaneAsync(target, autoPlay: target.Kind is FileKind.Audio or FileKind.Video);
                    break;
                case PaneCommand.Play when target is { Kind: FileKind.Audio or FileKind.Video }:
                    await ShowPreviewPaneAsync(target, autoPlay: true);
                    break;
                case PaneCommand.Edit when target is { IsDirectory: false }:
                    if (await RealPathAsync(target) is { } editPath) ShellActions.EditInNotepad(editPath);
                    break;
                case PaneCommand.Copy when selection.Count > 0 && pane.IsInArchive:
                    var tempFolder = Path.Combine(Path.GetTempPath(), "ZeroFileCommander", "clipboard", Guid.NewGuid().ToString("N")[..8]);
                    Directory.CreateDirectory(tempFolder);
                    await ArchiveService.ExtractAsync(selection, tempFolder, OverwriteMode.Overwrite);
                    await ClipboardService.SetFilesAsync(Directory.EnumerateFileSystemEntries(tempFolder), cut: false);
                    pane.ShowStatus($"{selection.Count} öğe arşivden kopyalandı");
                    break;
                case PaneCommand.Cut when selection.Count > 0:
                case PaneCommand.Copy when selection.Count > 0:
                    await ClipboardService.SetFilesAsync(selection.Select(i => i.FullPath), cut: command == PaneCommand.Cut);
                    pane.ShowStatus(command == PaneCommand.Cut ? $"{selection.Count} öğe kesildi" : $"{selection.Count} öğe kopyalandı");
                    break;
                case PaneCommand.Paste:
                    await PasteAsync(pane);
                    break;
                case PaneCommand.Rename when target is not null:
                    await RenameAsync(pane, target);
                    break;
                case PaneCommand.Delete:
                    // Shift basılıyken (Shift+Del, Shift+tık) kalıcı silme
                    await DeleteAsync(pane, selection, permanent: IsDown(VirtualKey.Shift));
                    break;
                case PaneCommand.CopyPath:
                    var paths = selection.Count > 0 ? selection.Select(i => i.FullPath) : new[] { pane.CurrentPath };
                    var text = string.Join(Environment.NewLine, paths.Where(p => p.Length > 0));
                    if (text.Length > 0)
                    {
                        ClipboardService.SetText(text);
                        pane.ShowStatus("Yol panoya kopyalandı");
                    }
                    break;
                case PaneCommand.CopyToOther:
                    await TransferAsync(pane, selection, move: false);
                    break;
                case PaneCommand.MoveToOther:
                    await TransferAsync(pane, selection, move: true);
                    break;
                case PaneCommand.Properties when target is { IsInArchive: true }:
                    await Dialogs.MessageAsync(Content.XamlRoot, DialogTheme, target.Name,
                        $"ZIP içinde · {(target.IsDirectory ? "Klasör" : target.SizeText)}\nDeğiştirilme: {target.ModifiedText}\nArşiv: {target.ArchivePath}");
                    break;
                case PaneCommand.Properties:
                    var subject = target ?? (pane.IsDriveList ? null : new FileItem
                    {
                        Name = Path.GetFileName(pane.CurrentPath.TrimEnd('\\')) is { Length: > 0 } n ? n : pane.CurrentPath,
                        FullPath = pane.CurrentPath,
                        IsDirectory = true,
                    });
                    if (subject is not null) await Dialogs.PropertiesAsync(Content.XamlRoot, DialogTheme, subject);
                    break;
                case PaneCommand.NewFolder:
                    await NewFolderAsync(pane);
                    break;
                case PaneCommand.Refresh:
                    pane.Refresh();
                    if (string.Equals(Other.CurrentPath, pane.CurrentPath, StringComparison.OrdinalIgnoreCase)) Other.Refresh();
                    break;
                case PaneCommand.OpenTerminal:
                    var folder = target is { IsDirectory: true } ? target.FullPath : pane.CurrentPath;
                    if (folder.Length > 0) ShellActions.OpenTerminal(folder);
                    break;
                case PaneCommand.ShowInExplorer when pane.IsInArchive:
                    ShellActions.ShowInExplorer(pane.ArchiveFile!);
                    break;
                case PaneCommand.ShowInExplorer:
                    if (target is not null) ShellActions.ShowInExplorer(target.FullPath);
                    else if (!pane.IsDriveList) ShellActions.ShowInExplorer(pane.CurrentPath);
                    break;
                case PaneCommand.RunAsAdmin when target is { IsDirectory: false }:
                    ShellActions.RunAsAdmin(target.FullPath);
                    break;
                case PaneCommand.Compress:
                    await CompressAsync(pane, selection);
                    break;
                case PaneCommand.ExtractHere when target is { Kind: FileKind.Archive, IsInArchive: false }:
                    await ExtractArchiveAsync(target, pane, pane);
                    break;
                case PaneCommand.ExtractToOther when target is { Kind: FileKind.Archive, IsInArchive: false }:
                    await ExtractArchiveAsync(target, pane, pane == LeftPane ? RightPane : LeftPane);
                    break;
            }
        }
        catch (Exception ex)
        {
            await Dialogs.MessageAsync(Content.XamlRoot, DialogTheme, "İşlem tamamlanamadı", ex.Message);
        }
    }

    private async Task RunAppCommandAsync(string tag)
    {
        switch (tag)
        {
            case "app:settings": await ShowSettingsAsync(); break;
            case "app:about": await Dialogs.AboutAsync(Content.XamlRoot, DialogTheme); break;
            case "app:shortcuts": await Dialogs.ShortcutsAsync(Content.XamlRoot, DialogTheme); break;
            case "app:github": await Launcher.LaunchUriAsync(new Uri("https://github.com/aliapaydin/zero-file-commander")); break;
            case "app:issues": await Launcher.LaunchUriAsync(new Uri("https://github.com/aliapaydin/zero-file-commander/issues/new/choose")); break;
            case "app:close": AppWindow_CloseRequestedByUser(); break;
            case "app:exit": ExitApplication(); break;
            case "app:hide": HideToTray(); break;
            case "app:downloads": ShowDownloads(); break;
            case "app:ribbon": _settings.ShowRibbon = !_settings.ShowRibbon; break;
            case "app:drives": _settings.ShowDriveBar = !_settings.ShowDriveBar; break;
            case "app:tree": _settings.ShowFolderTree = !_settings.ShowFolderTree; break;
            case "app:preview": _settings.ShowPreviewPane = !_settings.ShowPreviewPane; break;
            case "app:hover": _settings.HoverPreview = !_settings.HoverPreview; break;
            case "app:selectall": _active.FocusList(); SelectAll(_active, invert: false); break;
            case "app:invert": _active.FocusList(); SelectAll(_active, invert: true); break;
            case "app:samefolder": Other.Navigate(_active.CurrentPath); break;
            case "app:swap":
                var left = LeftPane.CurrentPath;
                LeftPane.Navigate(RightPane.CurrentPath);
                RightPane.Navigate(left);
                break;
            case "app:focusleft": SetActive(LeftPane); LeftPane.FocusList(); break;
            case "app:focusright": SetActive(RightPane); RightPane.FocusList(); break;
            case "app:resetlayout": ResetLayout(); break;
            case "app:newtab": _active.AddTab(); _active.FocusList(); break;
            case "app:closetab": _active.CloseCurrentTab(); _active.FocusList(); break;
            case "app:nexttab": _active.NextTab(1); _active.FocusList(); break;
            case "app:editpath": _active.BeginEditPath(); break;
            case "app:selectpattern": await _active.SelectByPatternAsync(select: true); break;
            case "app:deselectpattern": await _active.SelectByPatternAsync(select: false); break;
            case "app:log":
                Directory.CreateDirectory(CrashLog.FolderPath);
                if (!File.Exists(CrashLog.FilePath)) File.WriteAllText(CrashLog.FilePath, "Henüz kayıtlı hata yok.\r\n");
                ShellActions.Open(CrashLog.FilePath);
                break;
            default:
                if (tag.StartsWith("app:theme:") && int.TryParse(tag["app:theme:".Length..], out var theme))
                    _settings.Theme = (AppTheme)theme;
                break;
        }
    }

    private static void SelectAll(FilePane pane, bool invert)
    {
        // ListView'e erişim panelin içinde; basitçe tüm öğeleri yeniden seç.
        pane.SelectAll(invert);
    }

    // ================================================================ Dosya işlemleri

    private async Task TransferAsync(FilePane source, IReadOnlyList<FileItem> items, bool move)
    {
        if (items.Count == 0) return;
        var target = source == LeftPane ? RightPane : LeftPane;
        if (target.IsDriveList)
        {
            await Dialogs.MessageAsync(Content.XamlRoot, DialogTheme, "Hedef seçilmedi", "Karşı panelde bir klasör açın.");
            return;
        }
        if (string.Equals(source.CurrentPath, target.CurrentPath, StringComparison.OrdinalIgnoreCase))
        {
            await Dialogs.MessageAsync(Content.XamlRoot, DialogTheme, "Aynı klasör", "İki panel aynı klasörü gösteriyor.");
            return;
        }

        // Arşivden dışarı: çıkar
        if (source.IsInArchive)
        {
            if (target.IsInArchive)
            {
                await Dialogs.MessageAsync(Content.XamlRoot, DialogTheme, "Desteklenmiyor", "Bir arşivden diğerine doğrudan kopyalanamıyor; önce bir klasöre çıkarın.");
                return;
            }
            var mode = await AskOverwriteAsync(ArchiveService.CountConflicts(items, target.CurrentPath));
            if (mode is null) return;
            source.ShowStatus("Çıkarılıyor…");
            int extracted = await ArchiveService.ExtractAsync(items, target.CurrentPath, mode.Value);
            source.ShowStatus($"{extracted} dosya çıkarıldı");
            target.Refresh();
            return;
        }

        var sources = items.Select(i => i.FullPath).ToList();

        // Arşivin içine: ekle (taşımada kaynaklar sonra Geri Dönüşüm Kutusu'na)
        if (target.IsInArchive)
        {
            target.ShowStatus("Arşive ekleniyor…");
            await ArchiveService.AddAsync(target.ArchiveFile!, target.ArchiveInner, sources);
            if (move) await ShellFileOperations.RecycleAsync(Hwnd, sources);
            source.Refresh();
            target.Refresh();
            return;
        }

        if (move) Preview.Stop(); // taşınan medya dosyası kilitli kalmasın
        _ = move
            ? await ShellFileOperations.MoveAsync(Hwnd, sources, target.CurrentPath)
            : await ShellFileOperations.CopyAsync(Hwnd, sources, target.CurrentPath);

        source.Refresh();
        target.Refresh();
    }

    /// <summary>Çakışma varsa sorar: üzerine yaz / atla / vazgeç (null).</summary>
    private async Task<OverwriteMode?> AskOverwriteAsync(int conflicts)
    {
        if (conflicts == 0) return OverwriteMode.Overwrite;
        var dialog = Dialogs.Create(Content.XamlRoot, DialogTheme, "Bazı öğeler zaten var",
            new TextBlock { Text = $"Hedefte aynı adlı {conflicts} öğe var. Ne yapılsın?", TextWrapping = TextWrapping.Wrap },
            "Üzerine yaz", "Vazgeç", ContentDialogButton.Secondary);
        dialog.SecondaryButtonText = "Var olanları atla";
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => OverwriteMode.Overwrite,
            ContentDialogResult.Secondary => OverwriteMode.Skip,
            _ => null,
        };
    }

    private async Task CompressAsync(FilePane pane, IReadOnlyList<FileItem> items)
    {
        if (items.Count == 0 || !pane.IsFileSystemFolder) return;
        var baseName = items.Count == 1 ? Path.GetFileNameWithoutExtension(items[0].Name) : Path.GetFileName(pane.CurrentPath.TrimEnd('\\'));
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "Arşiv";
        var suggested = Path.GetFileName(ShellNew.UniquePath(Path.Combine(pane.CurrentPath, baseName + ".zip")));
        var name = await Dialogs.InputAsync(Content.XamlRoot, DialogTheme, "ZIP olarak sıkıştır", suggested, "Sıkıştır", selectStem: true);
        if (name is null) return;
        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name += ".zip";
        var archive = Path.Combine(pane.CurrentPath, name);
        if (File.Exists(archive))
        {
            await Dialogs.MessageAsync(Content.XamlRoot, DialogTheme, "Dosya zaten var", $"{name} adında bir dosya var; başka bir ad seçin.");
            return;
        }

        pane.ShowStatus($"{name} oluşturuluyor…");
        try
        {
            await ArchiveService.CreateAsync(archive, items.Select(i => i.FullPath));
        }
        catch
        {
            try { if (File.Exists(archive)) File.Delete(archive); } catch { /* yarım arşivi temizlemeyi dene */ }
            throw;
        }
        pane.Refresh();
        pane.SelectByName(name);
    }

    private async Task ExtractArchiveAsync(FileItem archive, FilePane source, FilePane destination)
    {
        if (!destination.IsFileSystemFolder)
        {
            await Dialogs.MessageAsync(Content.XamlRoot, DialogTheme, "Hedef seçilmedi", "Çıkarmak için hedef panelde bir klasör açın.");
            return;
        }
        var folderName = Path.GetFileName(ShellNew.UniquePath(Path.Combine(destination.CurrentPath, Path.GetFileNameWithoutExtension(archive.Name))));
        var target = Path.Combine(destination.CurrentPath, folderName);
        Directory.CreateDirectory(target);
        source.ShowStatus($"{archive.Name} çıkarılıyor…");
        int count = await ArchiveService.ExtractAllAsync(archive.FullPath, target, OverwriteMode.Overwrite);
        source.ShowStatus($"{count} dosya çıkarıldı → {folderName}");
        destination.Refresh();
        destination.SelectByName(folderName);
    }

    public async Task DropFilesAsync(FilePane target, IReadOnlyList<string> paths, bool copy)
    {
        if (target.IsDriveList || paths.Count == 0) return;

        if (target.IsInArchive)
        {
            SetActive(target);
            target.ShowStatus("Arşive ekleniyor…");
            await ArchiveService.AddAsync(target.ArchiveFile!, target.ArchiveInner, paths);
            target.Refresh();
            return;
        }

        // Zaten bu klasörde olanları atla.
        var incoming = paths.Where(p => !string.Equals(Path.GetDirectoryName(p.TrimEnd('\\')), target.CurrentPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)).ToList();
        if (incoming.Count == 0) return;

        SetActive(target);
        if (!copy) Preview.Stop();
        _ = copy
            ? await ShellFileOperations.CopyAsync(Hwnd, incoming, target.CurrentPath)
            : await ShellFileOperations.MoveAsync(Hwnd, incoming, target.CurrentPath);

        LeftPane.Refresh();
        RightPane.Refresh();
        if (incoming.Count == 1) target.SelectByName(Path.GetFileName(incoming[0].TrimEnd('\\')));
    }

    private async Task PasteAsync(FilePane pane)
    {
        if (pane.IsDriveList) return;
        var files = await ClipboardService.GetFilesAsync();
        if (files is null) return;

        if (pane.IsInArchive)
        {
            pane.ShowStatus("Arşive ekleniyor…");
            await ArchiveService.AddAsync(pane.ArchiveFile!, pane.ArchiveInner, files.Paths);
            pane.Refresh();
            return;
        }

        if (files.IsCut) Preview.Stop();
        bool ok = files.IsCut
            ? await ShellFileOperations.MoveAsync(Hwnd, files.Paths, pane.CurrentPath)
            : await ShellFileOperations.CopyAsync(Hwnd, files.Paths, pane.CurrentPath);
        if (ok && files.IsCut) ClipboardService.Clear();

        LeftPane.Refresh();
        RightPane.Refresh();
    }

    private async Task RenameAsync(FilePane pane, FileItem item)
    {
        var name = await Dialogs.InputAsync(Content.XamlRoot, DialogTheme, "Yeniden adlandır", item.Name, "Adlandır", selectStem: !item.IsDirectory);
        if (name is null || name == item.Name) return;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            await Dialogs.MessageAsync(Content.XamlRoot, DialogTheme, "Geçersiz ad", "Ad şu karakterleri içeremez: \\ / : * ? \" < > |");
            return;
        }

        var parent = Path.GetDirectoryName(item.FullPath.TrimEnd('\\'))!;
        var destination = Path.Combine(parent, name);
        Preview.Stop();
        if (item.IsDirectory) Directory.Move(item.FullPath, destination);
        else File.Move(item.FullPath, destination);

        pane.Refresh();
        pane.SelectByName(name);
        if (string.Equals(Other.CurrentPath, pane.CurrentPath, StringComparison.OrdinalIgnoreCase)) Other.Refresh();
    }

    private async Task NewFolderAsync(FilePane pane)
    {
        if (!pane.IsFileSystemFolder) return;
        var suggested = Path.GetFileName(ShellNew.UniquePath(Path.Combine(pane.CurrentPath, "Yeni klasör")));
        var name = await Dialogs.InputAsync(Content.XamlRoot, DialogTheme, "Yeni klasör", suggested, "Oluştur");
        if (name is null) return;
        Directory.CreateDirectory(Path.Combine(pane.CurrentPath, name));
        pane.Refresh();
        pane.SelectByName(name);
    }

    private async Task DeleteAsync(FilePane pane, IReadOnlyList<FileItem> items, bool permanent)
    {
        if (items.Count == 0) return;
        var subject = items.Count == 1 ? $"\"{items[0].Name}\"" : $"{items.Count} öğe";

        if (pane.IsInArchive)
        {
            if (!await Dialogs.ConfirmDangerAsync(Content.XamlRoot, DialogTheme, $"{subject} arşivden silinsin mi?",
                    "Arşivin içinden silinen öğeler Geri Dönüşüm Kutusu'na gitmez ve geri alınamaz.", "Arşivden sil")) return;
            await ArchiveService.DeleteAsync(items);
            pane.Refresh();
            return;
        }

        if (permanent)
        {
            if (!await Dialogs.ConfirmDangerAsync(Content.XamlRoot, DialogTheme,
                    $"{subject} kalıcı olarak silinsin mi?",
                    "Bu öğeler Geri Dönüşüm Kutusu'na gitmeyecek ve geri alınamayacak.",
                    "Kalıcı olarak sil")) return;
            Preview.Stop();
            await ShellFileOperations.DeletePermanentlyAsync(Hwnd, items.Select(i => i.FullPath));
        }
        else
        {
            if (!await Dialogs.ConfirmAsync(Content.XamlRoot, DialogTheme, $"{subject} silinsin mi?",
                    "Geri Dönüşüm Kutusu'na gönderilecek. Kalıcı silmek için Shift basılıyken sil.", "Sil")) return;
            Preview.Stop();
            await ShellFileOperations.RecycleAsync(Hwnd, items.Select(i => i.FullPath));
        }
        pane.Refresh();
        if (string.Equals(Other.CurrentPath, pane.CurrentPath, StringComparison.OrdinalIgnoreCase)) Other.Refresh();
    }

    private async Task ShowPreviewPaneAsync(FileItem item, bool autoPlay)
    {
        if (!_settings.ShowPreviewPane) _settings.ShowPreviewPane = true;
        await Preview.ShowAsync(await ResolvePreviewItemAsync(item), autoPlay);
    }

    // ================================================================ Şerit

    private void UpdateRibbon()
    {
        var selection = _active.SelectedItems;
        var item = selection.Count == 1 ? selection[0] : null;

        bool any = selection.Count > 0;
        bool single = selection.Count == 1;
        var kind = single ? item!.Kind : FileKind.Other;
        bool inFolder = !_active.IsDriveList;

        Show(RbNew, _active.IsFileSystemFolder);
        Show(RbCompress, any && _active.IsFileSystemFolder);
        Show(RbExtract, single && kind == FileKind.Archive && !item!.IsInArchive);
        Show(RbPaste, inFolder);
        RbPaste.IsEnabled = _canPaste;
        MenuPaste.IsEnabled = _canPaste;

        Show(RbSep1, any);
        Show(RbCut, any);
        Show(RbCopy, any);
        Show(RbRename, single);
        Show(RbDelete, any);
        Show(RbSep2, any);
        Show(RbCopyOther, any);
        Show(RbMoveOther, any);

        bool media = single && kind is FileKind.Audio or FileKind.Video;
        Show(RbPlay, media);
        RbPlay.Label = kind == FileKind.Video ? "Oynat" : "Çal";
        Show(RbPreview, single && kind is FileKind.Image or FileKind.Pdf or FileKind.Text);
        Show(RbEdit, single && kind == FileKind.Text);
        Show(RbOpenWith, single && !item!.IsDirectory);
        Show(RbAdmin, single && FileKinds.IsRunnable(item!));
        Show(RbTerminal, (single && kind == FileKind.Folder) || (!any && inFolder));
        Show(RbProperties, any || inFolder);
        Show(RbSep3, media || single);

        if (single)
        {
            ContextTag.Visibility = Visibility.Visible;
            ContextTagIcon.Glyph = FileKinds.Glyph(kind);
            ContextTagText.Text = FileKinds.Label(kind);
        }
        else if (selection.Count > 1)
        {
            ContextTag.Visibility = Visibility.Visible;
            ContextTagIcon.Glyph = "";
            ContextTagText.Text = $"{selection.Count} öğe";
        }
        else
        {
            ContextTag.Visibility = Visibility.Collapsed;
        }
    }

    private static void Show(UIElement element, bool visible) =>
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    // ================================================================ Klasör ağacı

    private sealed class FolderNode
    {
        public required string Name { get; init; }
        public required string Path { get; init; }
        public override string ToString() => Name;
    }

    private void BuildTreeRoots()
    {
        if (FolderTree.RootNodes.Count > 0) return;
        void AddRoot(string name, string path)
        {
            if (!Directory.Exists(path)) return;
            FolderTree.RootNodes.Add(new TreeViewNode
            {
                Content = new FolderNode { Name = name, Path = path },
                HasUnrealizedChildren = true,
            });
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AddRoot("Masaüstü", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddRoot("Belgeler", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddRoot("İndirilenler", Path.Combine(profile, "Downloads"));
        AddRoot("Resimler", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        AddRoot("Müzik", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        AddRoot("Videolar", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            AddRoot(drive.Name.TrimEnd('\\'), drive.RootDirectory.FullName);
        }
    }

    private void FolderTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (!args.Node.HasUnrealizedChildren || args.Node.Content is not FolderNode node) return;
        args.Node.HasUnrealizedChildren = false;
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            foreach (var dir in new DirectoryInfo(node.Path).EnumerateDirectories("*", options).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                bool hasChildren;
                try { hasChildren = dir.EnumerateDirectories("*", options).Any(); }
                catch { hasChildren = false; }
                args.Node.Children.Add(new TreeViewNode
                {
                    Content = new FolderNode { Name = dir.Name, Path = dir.FullName },
                    HasUnrealizedChildren = hasChildren,
                });
            }
        }
        catch
        {
            // erişim yok
        }
    }

    private void FolderTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        var node = args.InvokedItem as TreeViewNode;
        var folder = (node?.Content ?? args.InvokedItem) as FolderNode;
        if (folder is null) return;
        _active.Navigate(folder.Path);
    }

    // ================================================================ Ayarlar ve tema

    private void Settings_Changed(object? sender, string name)
    {
        if (name.StartsWith("Pane.", StringComparison.Ordinal)) return; // panelin kendi kayıtları
        DispatcherQueue.TryEnqueue(() =>
        {
            if (name is "Layout" or "All")
            {
                _placement.Restore(new Windows.Graphics.SizeInt32(1480, 900));
                SetLeftRatio(_settings.LeftRatio);
            }
            ApplyAllSettings();
            if (name == nameof(AppSettings.DownloadsEnabled)) UpdateDownloadIndicators();
        });
    }

    private void ApplyAllSettings()
    {
        // Tema
        var theme = ThemeHelper.ToElementTheme(_settings.Theme);
        if (RootGrid.RequestedTheme != theme) RootGrid.RequestedTheme = theme;
        _downloadsWindow?.ApplyTheme();
        MenuThemeSystem.IsChecked = _settings.Theme == AppTheme.System;
        MenuThemeLight.IsChecked = _settings.Theme == AppTheme.Light;
        MenuThemeDark.IsChecked = _settings.Theme == AppTheme.Dark;
        ApplyTitleBarColors();

        // Paneller
        RibbonArea.Visibility = _settings.ShowRibbon ? Visibility.Visible : Visibility.Collapsed;
        DriveBar.Visibility = _settings.ShowDriveBar ? Visibility.Visible : Visibility.Collapsed;
        MenuShowDrives.IsChecked = _settings.ShowDriveBar;
        MenuShowRibbon.IsChecked = _settings.ShowRibbon;

        bool tree = _settings.ShowFolderTree;
        if (tree) BuildTreeRoots();
        TreeHost.Visibility = tree ? Visibility.Visible : Visibility.Collapsed;
        TreeSplitter.Visibility = tree ? Visibility.Visible : Visibility.Collapsed;
        TreeColumn.Width = tree ? new GridLength(Math.Clamp(_settings.TreeWidth, 160, 600)) : new GridLength(0);
        TreeSplitColumn.Width = tree ? new GridLength(8) : new GridLength(0);
        MenuShowTree.IsChecked = tree;

        bool preview = _settings.ShowPreviewPane;
        bool wasVisible = Preview.Visibility == Visibility.Visible;
        Preview.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        PreviewSplitter.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        PreviewColumn.Width = preview ? new GridLength(Math.Clamp(_settings.PreviewWidth, 240, 1200)) : new GridLength(0);
        PreviewSplitColumn.Width = preview ? new GridLength(8) : new GridLength(0);
        Preview.OnVisibilityChanged();
        MenuShowPreview.IsChecked = preview;
        if (!preview && wasVisible) Preview.Stop();
        if (preview && !wasVisible) _ = UpdatePreviewAsync(autoPlay: false);

        MenuHover.IsChecked = _settings.HoverPreview;
        MenuDownloads.IsEnabled = true;

        // Tepsi
        if (_settings.RunInBackground || _settings.MinimizeToTray) _tray.Show();
        else _tray.Hide();
    }

    private void RootGrid_ActualThemeChanged(FrameworkElement sender, object args) => ApplyTitleBarColors();

    private void ApplyTitleBarColors()
    {
        var bar = AppWindow.TitleBar;
        bool dark = RootGrid.ActualTheme == ElementTheme.Dark;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonInactiveForegroundColor = dark ? ColorHelper.FromArgb(0xFF, 0x9A, 0x9A, 0x9A) : ColorHelper.FromArgb(0xFF, 0x80, 0x80, 0x80);
        bar.ButtonHoverBackgroundColor = dark ? ColorHelper.FromArgb(0x20, 0xFF, 0xFF, 0xFF) : ColorHelper.FromArgb(0x14, 0x00, 0x00, 0x00);
        bar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        bar.ButtonPressedBackgroundColor = dark ? ColorHelper.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : ColorHelper.FromArgb(0x24, 0x00, 0x00, 0x00);
    }

    private async Task ShowSettingsAsync()
    {
        var dialog = Dialogs.Create(Content.XamlRoot, DialogTheme, "Ayarlar", new SettingsView(Hwnd), close: "Kapat");
        await dialog.ShowAsync();
    }

    // ================================================================ Pano

    private async void Clipboard_ContentChanged(object? sender, object e)
    {
        _canPaste = ClipboardService.HasFiles();
        UpdateRibbon();

        if (!_settings.DownloadsEnabled || !_settings.CaptureClipboardLinks) return;
        var text = (await ClipboardService.GetTextAsync())?.Trim();
        if (!DownloadManager.LooksLikeDownloadUrl(text) || text == _lastCapturedUrl) return;
        _lastCapturedUrl = text;

        var url = text!;
        Notifier.Show(
            "İndirme bağlantısı bulundu",
            DownloadManager.GuessFileName(url),
            "",
            new NotificationAction("İndir", () => StartDownload(url), IsPrimary: true),
            new NotificationAction("Yoksay", () => { }));
    }

    // ================================================================ İndirmeler

    private void StartDownload(string url)
    {
        DownloadManager.Current.Start(url, _settings.DownloadFolder);
        UpdateDownloadIndicators();
    }

    private void ShowDownloads()
    {
        if (!_settings.DownloadsEnabled)
        {
            _ = EnableDownloadsPromptAsync();
            return;
        }
        if (_downloadsWindow is null)
        {
            _downloadsWindow = new DownloadsWindow();
            _downloadsWindow.Closed += (_, _) => _downloadsWindow = null;
        }
        _downloadsWindow.Activate();
        _downloadsWindow.FocusUrl();
    }

    private async Task EnableDownloadsPromptAsync()
    {
        if (!AppWindow.IsVisible) ShowFromTray();
        bool enable = await Dialogs.ConfirmAsync(Content.XamlRoot, DialogTheme, "İndirme yöneticisi kapalı",
            "İndirme yöneticisi varsayılan olarak kapalı gelir. Şimdi açılsın mı? Ayarlar'dan istediğin zaman kapatabilirsin.", "Aç");
        if (!enable) return;
        _settings.DownloadsEnabled = true;
        ShowDownloads();
    }

    private void UpdateDownloadIndicators()
    {
        var manager = DownloadManager.Current;
        double? progress = manager.TotalProgress;
        int active = manager.ActiveCount;

        _taskbar.Set(progress);

        bool show = _settings.DownloadsEnabled && active > 0;
        DownloadIndicator.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            DownloadIndicatorText.Text = active == 1 ? "1 indirme" : $"{active} indirme";
            bool unknown = progress is double p && double.IsNaN(p);
            DownloadIndicatorBar.IsIndeterminate = unknown;
            DownloadIndicatorBar.Value = unknown || progress is null ? 0 : progress.Value * 100;
        }

        _tray.SetTooltip(active > 0 && progress is double t && !double.IsNaN(t)
            ? $"Zero File Commander · {active} indirme · %{t * 100:0}"
            : "Zero File Commander");
    }

    private void Download_Completed(object? sender, DownloadItem item)
    {
        UpdateDownloadIndicators();
        Notifier.Show(
            "İndirme tamamlandı",
            $"{item.FileName} · {DownloadManager.FormatSize(item.ReceivedBytes)}",
            "",
            new NotificationAction("Aç", () => ShellActions.Open(item.FilePath), IsPrimary: true),
            new NotificationAction("Klasörde göster", () => ShellActions.ShowInExplorer(item.FilePath)));
    }

    // ================================================================ Sürücü çubuğu

    private sealed record DriveSnapshot(string Root, string Letter, string Label, DriveType Type, long Total, long Free);

    private async Task RefreshDrivesAsync(bool force = false)
    {
        if (!force && (DateTime.Now - _drivesRefreshed).TotalSeconds < 3) return;
        _drivesRefreshed = DateTime.Now;

        // Ağ sürücüleri yavaş yanıt verebilir; bilgileri arka planda topla.
        var drives = await Task.Run(() =>
        {
            var list = new List<DriveSnapshot>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? DriveTypeName(d.DriveType) : d.VolumeLabel;
                    list.Add(new DriveSnapshot(d.RootDirectory.FullName, d.Name.TrimEnd('\\'), label, d.DriveType, d.TotalSize, d.AvailableFreeSpace));
                }
                catch
                {
                    // hazır olmayan sürücü
                }
            }
            return list;
        });

        DrivePanel.Children.Clear();
        _driveButtons.Clear();
        foreach (var drive in drives)
        {
            var button = BuildDriveButton(drive);
            DrivePanel.Children.Add(button);
            _driveButtons.Add((drive.Root, button));
        }
        HighlightActiveDrive(_active);
    }

    private Button BuildDriveButton(DriveSnapshot drive)
    {
        double used = drive.Total > 0 ? (drive.Total - drive.Free) * 100.0 / drive.Total : 0;

        var icon = new FontIcon { Glyph = DriveGlyph(drive.Type), FontSize = 22 };
        var title = new TextBlock
        {
            Text = $"{drive.Letter}  {drive.Label}",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 170,
        };
        var bar = new ProgressBar
        {
            Value = used,
            Maximum = 100,
            Width = 150,
            MinHeight = 4,
            Margin = new Thickness(0, 4, 0, 2),
        };
        if (used >= 90) bar.ShowError = true;
        else if (used >= 75) bar.ShowPaused = true;
        var free = new TextBlock
        {
            Text = $"{Models.FileItem.FormatSize(drive.Free)} boş / {Models.FileItem.FormatSize(drive.Total)}",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.75,
        };

        var texts = new StackPanel();
        texts.Children.Add(title);
        texts.Children.Add(bar);
        texts.Children.Add(free);

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        content.Children.Add(icon);
        content.Children.Add(texts);

        var button = new Button
        {
            Content = content,
            Padding = new Thickness(12, 8, 14, 8),
            Tag = drive.Root,
            BorderThickness = new Thickness(1),
        };
        ToolTipService.SetToolTip(button, $"{drive.Letter} sürücüsünü aktif panelde aç · %{used:0} dolu");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{drive.Letter} {drive.Label}");
        button.Click += (_, _) =>
        {
            _active.Navigate(drive.Root);
            _active.FocusList();
        };
        return button;
    }

    private void HighlightActiveDrive(FilePane pane)
    {
        var path = pane.CurrentPath;
        var accent = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var normal = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"];
        foreach (var (root, button) in _driveButtons)
        {
            bool current = path.Length > 0 && path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            button.BorderBrush = current ? accent : normal;
            button.BorderThickness = new Thickness(current ? 2 : 1);
        }
    }

    private static string DriveGlyph(DriveType type) => type switch
    {
        DriveType.Removable => "\uE88E",
        DriveType.Network => "\uE8CE",
        DriveType.CDRom => "\uE958",
        _ => "\uEDA2",
    };

    private static string DriveTypeName(DriveType type) => type switch
    {
        DriveType.Removable => "Çıkarılabilir disk",
        DriveType.Network => "Ağ sürücüsü",
        DriveType.CDRom => "CD/DVD",
        DriveType.Ram => "RAM disk",
        _ => "Yerel disk",
    };

    // ================================================================ Yerleşim

    private void ResizePanes(double dx)
    {
        double left = LeftPane.ActualWidth;
        double right = RightPane.ActualWidth;
        double total = left + right;
        if (total <= 0) return;
        double newLeft = Math.Clamp(left + dx, 220, total - 220);
        LeftColumn.Width = new GridLength(newLeft, GridUnitType.Star);
        RightColumn.Width = new GridLength(total - newLeft, GridUnitType.Star);
    }

    private double CurrentLeftRatio()
    {
        double total = LeftColumn.Width.Value + RightColumn.Width.Value;
        return total <= 0 ? 0.5 : LeftColumn.Width.Value / total;
    }

    private void SetLeftRatio(double ratio)
    {
        ratio = Math.Clamp(ratio, 0.15, 0.85);
        LeftColumn.Width = new GridLength(ratio, GridUnitType.Star);
        RightColumn.Width = new GridLength(1 - ratio, GridUnitType.Star);
    }

    private void ResetLayout()
    {
        _settings.ResetLayout(); // Settings_Changed yeniden uygular
    }

    private void SaveSession()
    {
        _placement.Save();
        LeftPane.Save();
        RightPane.Save();
    }

    public void OpenInOtherPane(FilePane source, string path)
    {
        var target = source == LeftPane ? RightPane : LeftPane;
        target.AddTab(path);
        SetActive(target);
        target.FocusList();
    }

    public async Task CreateNewAsync(FilePane pane, ShellNewEntry? entry)
    {
        if (!pane.IsFileSystemFolder) return;
        if (entry is null)
        {
            await NewFolderAsync(pane);
            return;
        }

        var suggested = Path.GetFileName(ShellNew.UniquePath(Path.Combine(pane.CurrentPath, entry.BaseName + entry.Extension)));
        var name = await Dialogs.InputAsync(Content.XamlRoot, DialogTheme, $"Yeni {entry.DisplayName}", suggested, "Oluştur", selectStem: true);
        if (name is null) return;
        if (!name.EndsWith(entry.Extension, StringComparison.OrdinalIgnoreCase)) name += entry.Extension;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            await Dialogs.MessageAsync(Content.XamlRoot, DialogTheme, "Geçersiz ad", "Ad şu karakterleri içeremez: \\ / : * ? \" < > |");
            return;
        }

        try
        {
            var path = ShellNew.Create(entry, pane.CurrentPath, name);
            pane.Refresh();
            pane.SelectByName(Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            await Dialogs.MessageAsync(Content.XamlRoot, DialogTheme, "Dosya oluşturulamadı", ex.Message);
        }
    }

    // ================================================================ Tepsi ve pencere yaşam döngüsü

    private IReadOnlyList<TrayMenuItem> BuildTrayMenu()
    {
        var items = new List<TrayMenuItem>
        {
            new("Zero File Commander'ı aç", ShowFromTray, IsDefault: true),
        };
        if (_settings.DownloadsEnabled)
        {
            int active = DownloadManager.Current.ActiveCount;
            items.Add(new(active > 0 ? $"İndirmeler ({active} sürüyor)" : "İndirmeler", ShowDownloads));
        }
        items.Add(TrayMenuItem.Separator);
        items.Add(new("Ayarlar", () => { ShowFromTray(); _ = ShowSettingsAsync(); }));
        items.Add(new("Kapatınca arka planda çalış", () => _settings.RunInBackground = !_settings.RunInBackground, IsChecked: _settings.RunInBackground));
        items.Add(TrayMenuItem.Separator);
        items.Add(new("Çıkış", ExitApplication));
        return items;
    }

    private void ShowFromTray()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        Activate();
        _active.FocusList();
    }

    private void HideToTray()
    {
        SaveSession();
        _tray.Show();
        Preview.Stop();
        AppWindow.Hide();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            Notifier.Show("Arka planda çalışıyor", "Zero File Commander sistem tepsisinde. Tekrar açmak için simgesine tıkla.", "");
        }
    }

    private void AppWindow_CloseRequestedByUser()
    {
        if (_settings.RunInBackground) HideToTray();
        else ExitApplication();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exiting) return;
        if (_settings.RunInBackground)
        {
            args.Cancel = true;
            HideToTray();
            return;
        }
        ExitApplication();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPresenterChange && !args.DidSizeChange) return;
        if (_settings.MinimizeToTray && sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized })
        {
            HideToTray();
        }
    }

    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;

        foreach (var item in DownloadManager.Current.Items.Where(i => i.State == DownloadState.Downloading).ToList())
            DownloadManager.Current.Pause(item);

        SaveSession();
        Clipboard.ContentChanged -= Clipboard_ContentChanged;
        _taskbar.Set(null);
        _tray.Dispose();
        Preview.Stop();
        _downloadsWindow?.Close();
        Application.Current.Exit();
    }
}
