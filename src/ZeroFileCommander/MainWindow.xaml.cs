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
        LeftPane.Navigate(Directory.Exists(_settings.LeftPath) ? _settings.LeftPath : home);
        RightPane.Navigate(Directory.Exists(_settings.RightPath) ? _settings.RightPath : string.Empty);
        SetActive(LeftPane);
        LeftPane.FocusList();

        SetLeftRatio(_settings.LeftRatio);
        ApplyAllSettings();
        UpdateRibbon();
    }

    private FilePane Other => _active == LeftPane ? RightPane : LeftPane;

    private IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    private ElementTheme DialogTheme => RootGrid.ActualTheme;

    public bool CanPaste => _canPaste;

    // ================================================================ Panel yönetimi

    private void SetActive(FilePane pane)
    {
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
        UpdateRibbon();
        if (_settings.ShowPreviewPane)
        {
            _previewTimer.Stop();
            _previewTimer.Start();
        }
    }

    private async Task UpdatePreviewAsync(bool autoPlay)
    {
        if (!_settings.ShowPreviewPane) return;
        var item = _active.FocusedItem;
        await Preview.ShowAsync(item is { IsEntry: true } ? item : null, autoPlay);
    }

    private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool inText = FocusManager.GetFocusedElement(Content.XamlRoot) is TextBox;

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

        try
        {
            switch (command)
            {
                case PaneCommand.Open:
                    if (pane.FocusedItem is { } open) pane.Open(open);
                    break;
                case PaneCommand.OpenWith when target is { IsDirectory: false }:
                    ShellActions.OpenWith(target.FullPath);
                    break;
                case PaneCommand.Preview when target is not null:
                    if (target.IsDirectory) { pane.Open(target); break; }
                    await ShowPreviewPaneAsync(target, autoPlay: target.Kind is FileKind.Audio or FileKind.Video);
                    break;
                case PaneCommand.Play when target is { Kind: FileKind.Audio or FileKind.Video }:
                    await ShowPreviewPaneAsync(target, autoPlay: true);
                    break;
                case PaneCommand.Edit when target is { IsDirectory: false }:
                    ShellActions.EditInNotepad(target.FullPath);
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
                    await DeleteAsync(pane, selection);
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
                case PaneCommand.ShowInExplorer:
                    if (target is not null) ShellActions.ShowInExplorer(target.FullPath);
                    else if (!pane.IsDriveList) ShellActions.ShowInExplorer(pane.CurrentPath);
                    break;
                case PaneCommand.RunAsAdmin when target is { IsDirectory: false }:
                    ShellActions.RunAsAdmin(target.FullPath);
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

        var sources = items.Select(i => i.FullPath).ToList();
        if (move) Preview.Stop(); // taşınan medya dosyası kilitli kalmasın
        _ = move
            ? await ShellFileOperations.MoveAsync(Hwnd, sources, target.CurrentPath)
            : await ShellFileOperations.CopyAsync(Hwnd, sources, target.CurrentPath);

        source.Refresh();
        target.Refresh();
    }

    public async Task DropFilesAsync(FilePane target, IReadOnlyList<string> paths, bool copy)
    {
        if (target.IsDriveList || paths.Count == 0) return;

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
        if (pane.IsDriveList) return;
        var suggested = Path.GetFileName(ShellNew.UniquePath(Path.Combine(pane.CurrentPath, "Yeni klasör")));
        var name = await Dialogs.InputAsync(Content.XamlRoot, DialogTheme, "Yeni klasör", suggested, "Oluştur");
        if (name is null) return;
        Directory.CreateDirectory(Path.Combine(pane.CurrentPath, name));
        pane.Refresh();
        pane.SelectByName(name);
    }

    private async Task DeleteAsync(FilePane pane, IReadOnlyList<FileItem> items)
    {
        if (items.Count == 0) return;
        var title = items.Count == 1 ? $"\"{items[0].Name}\" silinsin mi?" : $"{items.Count} öğe silinsin mi?";
        if (!await Dialogs.ConfirmAsync(Content.XamlRoot, DialogTheme, title, "Geri Dönüşüm Kutusu'na gönderilecek.", "Sil")) return;

        Preview.Stop();
        await ShellFileOperations.RecycleAsync(Hwnd, items.Select(i => i.FullPath));
        pane.Refresh();
        if (string.Equals(Other.CurrentPath, pane.CurrentPath, StringComparison.OrdinalIgnoreCase)) Other.Refresh();
    }

    private async Task ShowPreviewPaneAsync(FileItem item, bool autoPlay)
    {
        if (!_settings.ShowPreviewPane) _settings.ShowPreviewPane = true;
        await Preview.ShowAsync(item, autoPlay);
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

        Show(RbNewFolder, inFolder);
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
        _settings.LeftPath = LeftPane.CurrentPath;
        _settings.RightPath = RightPane.CurrentPath;
    }

    public async Task CreateNewAsync(FilePane pane, ShellNewEntry? entry)
    {
        if (pane.IsDriveList) return;
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
