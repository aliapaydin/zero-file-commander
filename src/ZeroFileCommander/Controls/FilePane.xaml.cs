using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using ZeroFileCommander.Models;
using ZeroFileCommander.Services;
using ZeroFileCommander.Views;

namespace ZeroFileCommander.Controls;

public enum SortKey
{
    Name,
    Extension,
    Size,
    Date,
}

/// <summary>Bir paneldeki sekme: hangi klasörü gösterdiği.</summary>
public sealed class PaneTab
{
    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// Tek bir dosya paneli: sekmeler, yol çubuğu, sıralanabilir liste, yazarak filtreleme,
/// canlı yenileme. İki tanesi yan yana ana pencereyi oluşturur.
/// </summary>
public sealed partial class FilePane : UserControl
{
    public const string InternalPathsKey = "ZFC.Paths";
    public const string InternalSourceKey = "ZFC.Source";
    private const string DriveListToken = "::drives";

    private static readonly EnumerationOptions Enumeration = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = System.IO.FileAttributes.Hidden | System.IO.FileAttributes.System,
    };

    private readonly string _paneId = Guid.NewGuid().ToString("N");
    private readonly DispatcherQueueTimer _hoverTimer;
    private readonly DispatcherQueueTimer _refreshTimer;
    private readonly List<string> _crumbPaths = new();
    private List<FileItem> _all = new();
    private FileItem? _hoverItem;
    private Point _hoverPoint;
    private FileSystemWatcher? _watcher;
    private string _filter = string.Empty;
    private SortKey _sortKey = SortKey.Name;
    private bool _sortDescending;
    private bool _suppressTabEvents;

    public ObservableCollection<FileItem> Items { get; } = new();

    /// <summary>Boş string = "Bu bilgisayar" (sürücü listesi).</summary>
    public string CurrentPath { get; private set; } = string.Empty;

    public bool IsDriveList => CurrentPath.Length == 0;

    /// <summary>Panel bir ZIP arşivinin içini gösteriyorsa arşiv dosyası.</summary>
    public string? ArchiveFile { get; private set; }

    /// <summary>Arşiv içindeki klasör ("" = kök, aksi halde "/" ile biter).</summary>
    public string ArchiveInner { get; private set; } = string.Empty;

    public bool IsInArchive => ArchiveFile is not null;

    /// <summary>Gerçek bir dosya sistemi klasörü mü (sürücü listesi ya da arşiv değil).</summary>
    public bool IsFileSystemFolder => !IsDriveList && !IsInArchive;

    public IPaneHost? Host { get; set; }

    /// <summary>Ayarlarda sekmelerin saklandığı anahtar ön eki ("Left" / "Right").</summary>
    public string SettingsKey { get; set; } = "Left";

    /// <summary>Panel odak aldığında tetiklenir; ana pencere aktif paneli buna göre belirler.</summary>
    public event EventHandler? Activated;

    /// <summary>Seçim ya da klasör değiştiğinde tetiklenir (şerit ve önizleme için).</summary>
    public event EventHandler? SelectionChangedEx;

    public FilePane()
    {
        InitializeComponent();
        _hoverTimer = DispatcherQueue.CreateTimer();
        _hoverTimer.IsRepeating = false;
        _hoverTimer.Tick += HoverTimer_Tick;

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.IsRepeating = false;
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(350);
        _refreshTimer.Tick += (_, _) => Refresh();

        Unloaded += (_, _) => StopWatching();
        UpdateSortIndicators();
    }

    /// <summary>Seçili gerçek öğeler ("..", sürücüler hariç).</summary>
    public IReadOnlyList<FileItem> SelectedItems =>
        FileList.SelectedItems.Cast<FileItem>().Where(i => i.IsEntry).ToList();

    /// <summary>Klavye odağının (imlecin) bulunduğu öğe; yoksa ilk seçili öğe.</summary>
    public FileItem? FocusedItem
    {
        get
        {
            int index = FocusedIndex();
            if (index >= 0 && index < Items.Count) return Items[index];
            return FileList.SelectedItem as FileItem;
        }
    }

    public void FocusList()
    {
        int index = FocusedIndex();
        if (index >= 0 && FileList.ContainerFromIndex(index) is ListViewItem container)
            container.Focus(FocusState.Programmatic);
        else
            FileList.Focus(FocusState.Programmatic);
    }

    public void SetActive(bool active)
    {
        var key = active ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush";
        RootBorder.BorderBrush = (Brush)Application.Current.Resources[key];
        RootBorder.BorderThickness = new Thickness(active ? 1.5 : 1);
    }

    public void ShowStatus(string text) => StatusText.Text = text;

    // ================================================================ Sekmeler

    private PaneTab? CurrentTab => (Tabs.SelectedItem as TabViewItem)?.Tag as PaneTab;

    /// <summary>Kayıtlı sekmeleri ve sıralamayı geri yükler; yoksa <paramref name="fallback"/> açılır.</summary>
    public void Restore(string fallback)
    {
        var settings = AppSettings.Current;
        if (Enum.TryParse<SortKey>(settings.GetString($"Pane.{SettingsKey}.Sort", nameof(SortKey.Name)), out var key)) _sortKey = key;
        _sortDescending = settings.GetInt($"Pane.{SettingsKey}.SortDesc") == 1;
        UpdateSortIndicators();

        var saved = settings.GetString($"Pane.{SettingsKey}.Tabs")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p == DriveListToken || Directory.Exists(p))
            .ToList();
        if (saved.Count == 0) saved.Add(string.IsNullOrEmpty(fallback) ? DriveListToken : fallback);

        _suppressTabEvents = true;
        Tabs.TabItems.Clear();
        foreach (var path in saved) Tabs.TabItems.Add(CreateTabItem(path == DriveListToken ? string.Empty : path));
        int index = Math.Clamp(settings.GetInt($"Pane.{SettingsKey}.TabIndex"), 0, Tabs.TabItems.Count - 1);
        Tabs.SelectedIndex = index;
        _suppressTabEvents = false;

        UpdateClosable();
        Navigate(CurrentTab?.Path ?? string.Empty);
    }

    /// <summary>Sekmeleri ve sıralamayı ayarlara yazar.</summary>
    public void Save()
    {
        var settings = AppSettings.Current;
        var paths = Tabs.TabItems.OfType<TabViewItem>()
            .Select(t => t.Tag is PaneTab tab ? (tab.Path.Length == 0 ? DriveListToken : tab.Path) : null)
            .Where(p => p is not null);
        settings.SetString($"Pane.{SettingsKey}.Tabs", string.Join('\n', paths));
        settings.SetInt($"Pane.{SettingsKey}.TabIndex", Math.Max(0, Tabs.SelectedIndex));
        settings.SetString($"Pane.{SettingsKey}.Sort", _sortKey.ToString());
        settings.SetInt($"Pane.{SettingsKey}.SortDesc", _sortDescending ? 1 : 0);
    }

    /// <summary>Yeni sekme açar (varsayılan: bu sekmenin klasörü).</summary>
    public void AddTab(string? path = null)
    {
        var item = CreateTabItem(path ?? CurrentPath);
        Tabs.TabItems.Add(item);
        Tabs.SelectedItem = item; // SelectionChanged gezinir
        UpdateClosable();
    }

    public void CloseCurrentTab()
    {
        if (Tabs.SelectedItem is TabViewItem item) CloseTab(item);
    }

    public void NextTab(int delta)
    {
        int count = Tabs.TabItems.Count;
        if (count < 2) return;
        Tabs.SelectedIndex = ((Tabs.SelectedIndex + delta) % count + count) % count;
    }

    private TabViewItem CreateTabItem(string path)
    {
        var tab = new PaneTab { Path = path };
        var item = new TabViewItem
        {
            Tag = tab,
            IconSource = new FontIconSource { Glyph = path.Length == 0 ? "\uEDA2" : "\uE8B7" },
        };
        UpdateTabHeader(item);

        var menu = new MenuFlyout();
        var other = new MenuFlyoutItem { Text = "Karşı panelde aç", Icon = new FontIcon { Glyph = "\uE8AB" } };
        other.Click += (_, _) => Host?.OpenInOtherPane(this, tab.Path);
        var duplicate = new MenuFlyoutItem { Text = "Sekmeyi çoğalt", Icon = new FontIcon { Glyph = "\uE8C8" } };
        duplicate.Click += (_, _) => AddTab(tab.Path);
        var closeOthers = new MenuFlyoutItem { Text = "Diğer sekmeleri kapat" };
        closeOthers.Click += (_, _) => CloseOtherTabs(item);
        var close = new MenuFlyoutItem { Text = "Sekmeyi kapat", KeyboardAcceleratorTextOverride = "Ctrl+W" };
        close.Click += (_, _) => CloseTab(item);
        menu.Items.Add(other);
        menu.Items.Add(duplicate);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(closeOthers);
        menu.Items.Add(close);
        item.ContextFlyout = menu;
        return item;
    }

    private static void UpdateTabHeader(TabViewItem item)
    {
        if (item.Tag is not PaneTab tab) return;
        string name;
        if (tab.Path.Length == 0) name = "Bu bilgisayar";
        else
        {
            name = Path.GetFileName(tab.Path.TrimEnd('\\'));
            if (name.Length == 0) name = tab.Path; // sürücü kökü: "C:\"
        }
        item.Header = name;
        ToolTipService.SetToolTip(item, tab.Path.Length == 0 ? "Bu bilgisayar" : tab.Path);
        if (item.IconSource is FontIconSource icon) icon.Glyph = tab.Path.Length == 0 ? "\uEDA2" : "\uE8B7";
    }

    private void CloseTab(TabViewItem item)
    {
        if (Tabs.TabItems.Count <= 1) return;
        int index = Tabs.TabItems.IndexOf(item);
        bool wasSelected = ReferenceEquals(Tabs.SelectedItem, item);
        Tabs.TabItems.Remove(item);
        if (wasSelected) Tabs.SelectedIndex = Math.Clamp(index, 0, Tabs.TabItems.Count - 1);
        UpdateClosable();
    }

    private void CloseOtherTabs(TabViewItem keep)
    {
        foreach (var other in Tabs.TabItems.OfType<TabViewItem>().Where(t => !ReferenceEquals(t, keep)).ToList())
            Tabs.TabItems.Remove(other);
        Tabs.SelectedItem = keep;
        UpdateClosable();
    }

    private void UpdateClosable()
    {
        bool closable = Tabs.TabItems.Count > 1;
        foreach (var item in Tabs.TabItems.OfType<TabViewItem>()) item.IsClosable = closable;
    }

    private void Tabs_AddTabButtonClick(TabView sender, object args) => AddTab();

    private void Tabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args) => CloseTab(args.Tab);

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTabEvents || CurrentTab is not { } tab) return;
        if (!string.Equals(tab.Path, CurrentPath, StringComparison.OrdinalIgnoreCase)) Navigate(tab.Path);
        Activated?.Invoke(this, EventArgs.Empty);
    }

    // ================================================================ Gezinme ve liste

    public void Navigate(string? path)
    {
        path ??= string.Empty;
        var previous = CurrentPath;
        bool sameFolder = string.Equals(previous, path, StringComparison.OrdinalIgnoreCase) && Items.Count > 0;

        List<FileItem> loaded;
        string? archive = null;
        string inner = string.Empty;
        try
        {
            if (path.Length == 0)
            {
                loaded = LoadDrives();
            }
            else if (Directory.Exists(path))
            {
                loaded = LoadFolder(path, out path);
            }
            else if (ArchiveService.TrySplit(path, out archive, out inner))
            {
                loaded = ArchiveService.List(archive, inner);
                path = ArchiveService.DisplayPath(archive, inner);
            }
            else
            {
                throw new DirectoryNotFoundException($"Klasör bulunamadı: {path}");
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            return;
        }

        if (!sameFolder)
        {
            _filter = string.Empty;
            UpdateFilterBar();
        }
        else
        {
            // Değişmeyen öğeleri koru: seçim ve kaydırma konumu bozulmaz.
            var old = new Dictionary<string, FileItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var existingItem in _all) old.TryAdd(existingItem.FullPath, existingItem);
            for (int i = 0; i < loaded.Count; i++)
            {
                if (old.TryGetValue(loaded[i].FullPath, out var existing) && existing.SameAs(loaded[i])) loaded[i] = existing;
            }
        }

        CurrentPath = path;
        ArchiveFile = archive;
        ArchiveInner = inner;
        _all = loaded;
        if (CurrentTab is { } tab)
        {
            tab.Path = path;
            if (Tabs.SelectedItem is TabViewItem tabItem) UpdateTabHeader(tabItem);
        }
        UpdateCrumbs();
        ApplyView(resetItems: !sameFolder);

        if (!sameFolder)
        {
            // Üst klasöre çıkınca geldiğimiz klasörü seçili getir (Total Commander davranışı).
            var reselect = Items.FirstOrDefault(i => !i.IsParent && string.Equals(i.FullPath.TrimEnd('\\'), previous.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            FileList.SelectedItem = reselect ?? Items.FirstOrDefault();
            if (FileList.SelectedItem is not null) FileList.ScrollIntoView(FileList.SelectedItem);
            if (archive is null) Watch(path);
            else StopWatching();
        }

        UpdateStatus();
        SelectionChangedEx?.Invoke(this, EventArgs.Empty);
        UpdateGit(force: sameFolder);
    }

    public void Refresh() => Navigate(CurrentPath);

    // ================================================================ Git durumu

    private int _gitVersion;
    private GitInfo? _git;

    /// <summary>Açık klasör bir Git deposundaysa satırlara durum işaretlerini, yol çubuğuna dalı koyar.</summary>
    public async void UpdateGit(bool force = false)
    {
        int version = ++_gitVersion;
        try
        {
            string path = CurrentPath;
            if (IsDriveList || IsInArchive || !AppSettings.Current.ShowGitStatus || IsSlowPath(path))
            {
                ClearGit();
                return;
            }
            var root = await Task.Run(() => GitService.FindRoot(path));
            if (version != _gitVersion) return;
            if (root is null)
            {
                ClearGit();
                return;
            }
            var info = await GitService.GetAsync(root, force);
            if (version != _gitVersion) return;
            if (info is null)
            {
                ClearGit();
                return;
            }

            _git = info;
            foreach (var item in _all)
                item.Git = item.IsEntry ? info.StateOf(item.FullPath, item.IsDirectory) : GitState.None;

            GitBranchText.Text = info.Summary;
            GitChangesText.Text = info.HasStatus && info.ChangeCount > 0 ? $"±{info.ChangeCount}" : string.Empty;
            ToolTipService.SetToolTip(GitPill, info.HasStatus
                ? $"Git: {info.Branch}{(info.HasUpstream ? string.Empty : " (uzak dal yok)")}\n{info.ChangeCount} değişiklik · {info.Root}"
                : $"Git: {info.Branch}\ngit.exe bulunamadı; dosya durumları gösterilemiyor.");
            GitPill.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Git durumu", ex);
            if (version == _gitVersion) ClearGit();
        }
    }

    private void ClearGit()
    {
        _git = null;
        GitPill.Visibility = Visibility.Collapsed;
        foreach (var item in _all) item.Git = GitState.None;
    }

    private static bool IsSlowPath(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) return true;
            return new DriveInfo(root).DriveType is DriveType.Network or DriveType.CDRom;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private void GitPill_Click(object sender, RoutedEventArgs e)
    {
        if (_git is not { } git) return;
        var menu = new MenuFlyout();
        var changed = new MenuFlyoutItem { Text = "Değişenleri seç", Icon = new FontIcon { Glyph = "\uE762" }, IsEnabled = git.HasStatus };
        changed.Click += (_, _) =>
        {
            int count = SelectWhere(i => i.Git is GitState.Modified or GitState.Added or GitState.Deleted or GitState.Untracked or GitState.Conflict);
            ShowStatus(count == 0 ? "Bu klasörde değişen dosya yok" : $"{count} değişen öğe seçildi");
        };
        var root = new MenuFlyoutItem { Text = "Depo köküne git", Icon = new FontIcon { Glyph = "\uE80F" } };
        root.Click += (_, _) => Navigate(git.Root);
        var copy = new MenuFlyoutItem { Text = "Dal adını kopyala", Icon = new FontIcon { Glyph = "\uE8C8" } };
        copy.Click += (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(git.Branch);
            Clipboard.SetContent(package);
        };
        var refresh = new MenuFlyoutItem { Text = "Durumu yenile", Icon = new FontIcon { Glyph = "\uE72C" } };
        refresh.Click += (_, _) => UpdateGit(force: true);
        menu.Items.Add(changed);
        menu.Items.Add(root);
        menu.Items.Add(copy);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(refresh);
        menu.ShowAt(GitPill, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight });
    }

    public void GoUp()
    {
        if (IsDriveList) return;
        Navigate(Directory.GetParent(CurrentPath)?.FullName ?? string.Empty);
    }

    public void SelectByName(string name)
    {
        var item = Items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;
        FileList.SelectedItems.Clear();
        FileList.SelectedItem = item;
        FileList.ScrollIntoView(item);
    }

    /// <summary>Koşula uyan öğeleri işaretler (önceki işaretler kalkar); işaretlenen sayıyı döner.</summary>
    public int SelectWhere(Func<FileItem, bool> predicate)
    {
        FileList.SelectedItems.Clear();
        int count = 0;
        foreach (var item in Items.Where(i => i.IsEntry && predicate(i)))
        {
            FileList.SelectedItems.Add(item);
            count++;
        }
        return count;
    }

    /// <summary>Panelde şu an listelenen gerçek öğeler.</summary>
    public IReadOnlyList<FileItem> Entries => Items.Where(i => i.IsEntry).ToList();

    public void SelectAll(bool invert)
    {
        var current = new HashSet<FileItem>(FileList.SelectedItems.Cast<FileItem>());
        FileList.SelectedItems.Clear();
        foreach (var item in Items.Where(i => i.IsEntry))
        {
            if (!invert || !current.Contains(item)) FileList.SelectedItems.Add(item);
        }
    }

    /// <summary>"*.pdf; rapor*" gibi desenlere uyan öğeleri seçer ya da seçimden çıkarır.</summary>
    public async Task SelectByPatternAsync(bool select)
    {
        if (XamlRoot is null) return;
        var input = await Dialogs.InputAsync(XamlRoot, ActualTheme, select ? "Desenle seç" : "Desenle seçimi kaldır",
            AppSettings.Current.GetString("Pane.LastPattern", "*.*"), select ? "Seç" : "Kaldır");
        if (input is null) return;
        AppSettings.Current.SetString("Pane.LastPattern", input);

        var regexes = input.Split([';', ' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => new Regex("^" + Regex.Escape(p).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase))
            .ToList();
        foreach (var item in Items.Where(i => i.IsEntry && regexes.Any(r => r.IsMatch(i.Name))))
        {
            bool selected = FileList.SelectedItems.Contains(item);
            if (select && !selected) FileList.SelectedItems.Add(item);
            else if (!select && selected) FileList.SelectedItems.Remove(item);
        }
        FocusList();
    }

    public void Open(FileItem item)
    {
        if (item.IsDirectory)
            Navigate(item.FullPath);
        else if (item.IsInArchive)
            _ = OpenArchiveEntryAsync(item);
        else if (ArchiveService.IsArchive(item.FullPath))
            Navigate(item.FullPath); // ZIP'e klasör gibi gir
        else
            ShellActions.Open(item.FullPath);
    }

    private async Task OpenArchiveEntryAsync(FileItem item)
    {
        try
        {
            StatusText.Text = $"{item.Name} çıkarılıyor…";
            var temp = await ArchiveService.ExtractToTempAsync(item);
            ShellActions.Open(temp);
            UpdateStatus();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    public void BeginEditPath()
    {
        PathBox.Text = IsDriveList ? string.Empty : CurrentPath;
        CrumbHost.Visibility = Visibility.Collapsed;
        PathBox.Visibility = Visibility.Visible;
        PathBox.Focus(FocusState.Programmatic);
        PathBox.SelectAll();
    }

    private static List<FileItem> LoadDrives()
    {
        var list = new List<FileItem>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            string label;
            try
            {
                label = drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? $"{drive.Name.TrimEnd('\\')}  {drive.VolumeLabel}"
                    : drive.Name.TrimEnd('\\');
            }
            catch
            {
                label = drive.Name.TrimEnd('\\');
            }
            list.Add(new FileItem { Name = label, FullPath = drive.RootDirectory.FullName, IsDirectory = true, IsDrive = true });
        }
        return list;
    }

    private static List<FileItem> LoadFolder(string path, out string normalized)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) throw new DirectoryNotFoundException($"Klasör bulunamadı: {path}");
        normalized = dir.FullName;

        var list = new List<FileItem>
        {
            new() { Name = "..", FullPath = dir.Parent?.FullName ?? string.Empty, IsDirectory = true, IsParent = true },
        };
        foreach (var d in dir.EnumerateDirectories("*", Enumeration))
            list.Add(new FileItem { Name = d.Name, FullPath = d.FullName, IsDirectory = true, Modified = d.LastWriteTime });
        foreach (var f in dir.EnumerateFiles("*", Enumeration))
            list.Add(new FileItem { Name = f.Name, FullPath = f.FullName, Size = f.Length, Modified = f.LastWriteTime });
        return list;
    }

    /// <summary>Sıralama ve filtreyi uygular; listeyi en az değişiklikle günceller.</summary>
    private void ApplyView(bool resetItems)
    {
        IEnumerable<FileItem> view = _all;
        if (_filter.Length > 0)
            view = view.Where(i => i.IsParent || i.Name.Contains(_filter, StringComparison.CurrentCultureIgnoreCase));
        var target = Sort(view).ToList();

        if (resetItems)
        {
            Items.Clear();
            foreach (var item in target) Items.Add(item);
            return;
        }

        var keepSelection = FileList.SelectedItems.Cast<FileItem>().ToList();
        for (int i = 0; i < target.Count; i++)
        {
            var wanted = target[i];
            if (i < Items.Count && ReferenceEquals(Items[i], wanted)) continue;
            int existing = -1;
            for (int j = i + 1; j < Items.Count; j++)
            {
                if (ReferenceEquals(Items[j], wanted)) { existing = j; break; }
            }
            if (existing >= 0) Items.Move(existing, i);
            else Items.Insert(i, wanted);
        }
        while (Items.Count > target.Count) Items.RemoveAt(Items.Count - 1);

        // Taşıma sırasında kaybolan seçimleri geri getir.
        foreach (var item in keepSelection)
        {
            if (Items.Contains(item) && !FileList.SelectedItems.Contains(item)) FileList.SelectedItems.Add(item);
        }
    }

    private IEnumerable<FileItem> Sort(IEnumerable<FileItem> items)
    {
        var list = items.ToList();
        var parent = list.Where(i => i.IsParent);
        var drives = list.Where(i => i.IsDrive).OrderBy(i => i.FullPath, StringComparer.OrdinalIgnoreCase);
        var folders = list.Where(i => i.IsDirectory && i.IsEntry);
        var files = list.Where(i => !i.IsDirectory);

        var nameComparer = StringComparer.Create(System.Globalization.CultureInfo.CurrentCulture, ignoreCase: true);
        IOrderedEnumerable<FileItem> OrderFiles(IEnumerable<FileItem> source) => _sortKey switch
        {
            SortKey.Extension => _sortDescending
                ? source.OrderByDescending(i => i.Extension, nameComparer).ThenBy(i => i.Name, nameComparer)
                : source.OrderBy(i => i.Extension, nameComparer).ThenBy(i => i.Name, nameComparer),
            SortKey.Size => _sortDescending
                ? source.OrderByDescending(i => i.Size ?? 0).ThenBy(i => i.Name, nameComparer)
                : source.OrderBy(i => i.Size ?? 0).ThenBy(i => i.Name, nameComparer),
            SortKey.Date => _sortDescending
                ? source.OrderByDescending(i => i.Modified).ThenBy(i => i.Name, nameComparer)
                : source.OrderBy(i => i.Modified).ThenBy(i => i.Name, nameComparer),
            _ => _sortDescending
                ? source.OrderByDescending(i => i.Name, nameComparer)
                : source.OrderBy(i => i.Name, nameComparer),
        };
        // Klasörler her zaman üstte; boyutları olmadığı için boyuta göre sıralamada ada göre dizilir.
        var orderedFolders = _sortKey == SortKey.Date
            ? OrderFiles(folders)
            : (_sortDescending && _sortKey == SortKey.Name
                ? folders.OrderByDescending(i => i.Name, nameComparer)
                : folders.OrderBy(i => i.Name, nameComparer));

        return parent.Concat(drives).Concat(orderedFolders).Concat(OrderFiles(files));
    }

    private void SortHeader_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string tag || !Enum.TryParse<SortKey>(tag, out var key)) return;
        if (key == _sortKey) _sortDescending = !_sortDescending;
        else
        {
            _sortKey = key;
            _sortDescending = key is SortKey.Size or SortKey.Date; // büyükler/yeniler önce
        }
        UpdateSortIndicators();
        ApplyView(resetItems: false);
        if (FileList.SelectedItem is not null) FileList.ScrollIntoView(FileList.SelectedItem);
        AppSettings.Current.SetString($"Pane.{SettingsKey}.Sort", _sortKey.ToString());
        AppSettings.Current.SetInt($"Pane.{SettingsKey}.SortDesc", _sortDescending ? 1 : 0);
    }

    private void UpdateSortIndicators()
    {
        string arrow = _sortDescending ? "\uE70D" : "\uE70E";
        SortName.Glyph = _sortKey == SortKey.Name ? arrow : string.Empty;
        SortExtension.Glyph = _sortKey == SortKey.Extension ? arrow : string.Empty;
        SortSize.Glyph = _sortKey == SortKey.Size ? arrow : string.Empty;
        SortDate.Glyph = _sortKey == SortKey.Date ? arrow : string.Empty;
    }

    private void UpdateStatus()
    {
        var selected = SelectedItems;
        if (selected.Count > 1)
        {
            long bytes = selected.Where(i => !i.IsDirectory).Sum(i => i.Size ?? 0);
            int folders = selected.Count(i => i.IsDirectory);
            StatusText.Text = $"{selected.Count} öğe işaretli · {FileItem.FormatSize(bytes)}" + (folders > 0 ? $" ({folders} klasör hariç)" : string.Empty);
            return;
        }
        int dirs = _all.Count(i => i.IsDirectory && !i.IsParent);
        int files = _all.Count(i => !i.IsDirectory);
        string text = IsDriveList ? $"{dirs} sürücü" : $"{dirs} klasör, {files} dosya";
        if (IsInArchive) text = $"ZIP · {text}";
        if (_filter.Length > 0) text += $" · filtre: {Items.Count(i => !i.IsParent)} eşleşme";
        StatusText.Text = text;
    }

    private int FocusedIndex()
    {
        if (XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is ListViewItem container)
        {
            int index = FileList.IndexFromContainer(container);
            if (index >= 0) return index;
        }
        return FileList.SelectedIndex;
    }

    // ================================================================ Canlı yenileme

    private void Watch(string path)
    {
        StopWatching();
        if (path.Length == 0) return;
        try
        {
            var root = Path.GetPathRoot(path);
            if (root is not null && !root.StartsWith(@"\\", StringComparison.Ordinal))
            {
                var type = new DriveInfo(root).DriveType;
                if (type is DriveType.Network or DriveType.CDRom) return; // ağ/CD'de izleme yavaş ve güvenilmez
            }
            else if (root is not null)
            {
                return; // UNC yolu
            }

            _watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
            };
            _watcher.Created += Watcher_Changed;
            _watcher.Deleted += Watcher_Changed;
            _watcher.Renamed += Watcher_Changed;
            _watcher.Changed += Watcher_Changed;
            _watcher.Error += (_, _) => DispatcherQueue.TryEnqueue(StopWatching);
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            StopWatching();
        }
    }

    private void StopWatching()
    {
        if (_watcher is null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }

    private void Watcher_Changed(object sender, FileSystemEventArgs e)
    {
        // Olaylar art arda gelir; kısa bir süre bekleyip tek seferde yenile.
        DispatcherQueue.TryEnqueue(() =>
        {
            _refreshTimer.Stop();
            _refreshTimer.Start();
        });
    }

    // ================================================================ Yol çubuğu

    private void UpdateCrumbs()
    {
        var names = new List<string> { "Bu bilgisayar" };
        _crumbPaths.Clear();
        _crumbPaths.Add(string.Empty);
        if (!IsDriveList)
        {
            var parts = new Stack<DirectoryInfo>();
            for (var d = new DirectoryInfo(CurrentPath); d is not null; d = d.Parent) parts.Push(d);
            foreach (var d in parts)
            {
                names.Add(d.Parent is null ? d.FullName.TrimEnd('\\') : d.Name);
                _crumbPaths.Add(d.FullName);
            }
        }
        Crumbs.ItemsSource = names;
        // Son parçayı görünür tut.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => CrumbScroller.ChangeView(CrumbScroller.ScrollableWidth, null, null, true));
    }

    private void Crumbs_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Index >= 0 && args.Index < _crumbPaths.Count) Navigate(_crumbPaths[args.Index]);
        FocusList();
    }

    private void CrumbHost_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Parçaların dışındaki boş alana tıklayınca düzenleme moduna geç.
        if (ReferenceEquals(e.OriginalSource, CrumbHost) || e.OriginalSource is ScrollViewer or Border { Child: ScrollViewer })
            BeginEditPath();
    }

    private void EditPath_Click(object sender, RoutedEventArgs e) => BeginEditPath();

    private void EndEditPath()
    {
        PathBox.Visibility = Visibility.Collapsed;
        CrumbHost.Visibility = Visibility.Visible;
    }

    private void PathBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            EndEditPath();
            FocusList();
            e.Handled = true;
            return;
        }
        if (e.Key != VirtualKey.Enter) return;
        var text = PathBox.Text.Trim().Trim('"');
        EndEditPath();
        Navigate(text.Length == 0 || text == "Bu bilgisayar" ? string.Empty : Environment.ExpandEnvironmentVariables(text));
        FocusList();
        e.Handled = true;
    }

    private void PathBox_LostFocus(object sender, RoutedEventArgs e) => EndEditPath();

    // ================================================================ Klavye ve olaylar

    private void OnGotFocus(object sender, RoutedEventArgs e) => Activated?.Invoke(this, EventArgs.Empty);

    private void UpButton_Click(object sender, RoutedEventArgs e) => GoUp();

    private void List_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileItem item) Open(item);
        else if (FocusedItem is { } focused) Open(focused);
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void List_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = IsDown(VirtualKey.Control);
        switch (e.Key)
        {
            case VirtualKey.Enter when FocusedItem is { } item:
                Open(item);
                e.Handled = true;
                break;
            case VirtualKey.Back:
                if (_filter.Length > 0)
                {
                    _filter = _filter[..^1];
                    UpdateFilter();
                }
                else
                {
                    GoUp();
                }
                e.Handled = true;
                break;
            case VirtualKey.Escape when _filter.Length > 0:
                _filter = string.Empty;
                UpdateFilter();
                e.Handled = true;
                break;
            case VirtualKey.Space when FocusedItem is { IsEntry: true } && _filter.Length == 0:
                Host?.Execute(PaneCommand.Preview, this);
                e.Handled = true;
                break;
            case VirtualKey.Insert:
                ToggleMarkAndAdvance();
                e.Handled = true;
                break;
            case VirtualKey.Add when !ctrl:
                _ = SelectByPatternAsync(select: true);
                e.Handled = true;
                break;
            case VirtualKey.Subtract when !ctrl:
                _ = SelectByPatternAsync(select: false);
                e.Handled = true;
                break;
            case VirtualKey.Multiply:
                SelectAll(invert: true);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Insert: imlecin üzerindeki öğeyi işaretle/kaldır ve bir aşağı in.</summary>
    private void ToggleMarkAndAdvance()
    {
        int index = FocusedIndex();
        if (index < 0 || index >= Items.Count) return;
        var item = Items[index];
        if (item.IsEntry)
        {
            if (FileList.SelectedItems.Contains(item)) FileList.SelectedItems.Remove(item);
            else FileList.SelectedItems.Add(item);
        }
        int next = Math.Min(index + 1, Items.Count - 1);
        FileList.ScrollIntoView(Items[next]);
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (FileList.ContainerFromIndex(next) is ListViewItem container) container.Focus(FocusState.Keyboard);
        });
    }

    /// <summary>Yazılan harfler listeyi filtreler.</summary>
    private void List_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
    {
        char c = args.Character;
        if (char.IsControl(c) || IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu)) return;
        if (c == ' ' && _filter.Length == 0) return; // Space önizleme
        if (c is '+' or '-' or '*' && _filter.Length == 0) return; // desen kısayolları
        _filter += c;
        UpdateFilter();
        args.Handled = true;
    }

    private void UpdateFilter()
    {
        UpdateFilterBar();
        ApplyView(resetItems: false);
        if (FileList.SelectedItems.Count == 0 || FileList.SelectedItem is FileItem { IsParent: true })
        {
            var first = Items.FirstOrDefault(i => !i.IsParent);
            if (first is not null)
            {
                FileList.SelectedItem = first;
                FileList.ScrollIntoView(first);
            }
        }
        UpdateStatus();
    }

    private void UpdateFilterBar()
    {
        FilterBar.Visibility = _filter.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        FilterText.Text = _filter;
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateStatus();
        SelectionChangedEx?.Invoke(this, EventArgs.Empty);
    }

    private void List_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not FileItem item || item.IsParent || item.Icon is not null) return;
        // Simgeler yalnızca ekrana gelen satırlar için, ikinci aşamada yüklenir.
        if (args.Phase == 0)
        {
            args.RegisterUpdateCallback(1, (_, a) =>
            {
                if (a.Item is FileItem f && f.Icon is null) f.Icon = ShellIcons.Get(f);
            });
        }
    }

    // ---------------------------------------------------------------- Sağ tık menüsü

    private void List_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        HideHover();
        var item = (e.OriginalSource as FrameworkElement)?.DataContext as FileItem;

        if (item is not null && !FileList.SelectedItems.Contains(item))
        {
            FileList.SelectedItems.Clear();
            FileList.SelectedItem = item;
        }
        else if (item is null)
        {
            FileList.SelectedItems.Clear();
        }

        FocusList();
        Activated?.Invoke(this, EventArgs.Empty);

        var menu = BuildContextMenu(item is { IsEntry: true } ? item : null);
        // Menüyü pencere içinde çiz: ayrı açılır pencere olunca imleç, kenarındaki
        // boyutlandırma alanına denk gelip ↕ şekline dönüyordu.
        menu.ShouldConstrainToRootBounds = true;
        Overlay.Track(menu);
        menu.Opened += (_, _) => CursorHelper.ApplyToOpenPopups(XamlRoot);
        // Menü, tıklanan noktadan sağa-aşağı açılır (sol üst köşesi imleçte).
        menu.ShowAt(FileList, new FlyoutShowOptions
        {
            Position = e.GetPosition(FileList),
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            ShowMode = FlyoutShowMode.Standard,
        });
        e.Handled = true;
    }

    private CommandBarFlyout BuildContextMenu(FileItem? item)
    {
        var menu = new CommandBarFlyout { AlwaysExpanded = true };
        bool canPaste = Host?.CanPaste ?? false;

        if (item is not null)
        {
            var selection = SelectedItems;
            bool single = selection.Count <= 1;

            menu.PrimaryCommands.Add(MenuButton("Kes", "", PaneCommand.Cut, "Ctrl+X"));
            menu.PrimaryCommands.Add(MenuButton("Kopyala", "", PaneCommand.Copy, "Ctrl+C"));
            menu.PrimaryCommands.Add(MenuButton("Yapıştır", "", PaneCommand.Paste, "Ctrl+V", enabled: canPaste));
            menu.PrimaryCommands.Add(MenuButton("Yeniden adlandır", "", PaneCommand.Rename, "F2", enabled: single));
            menu.PrimaryCommands.Add(MenuButton("Sil", "", PaneCommand.Delete, "Del · Shift+Del: kalıcı"));

            menu.SecondaryCommands.Add(MenuButton("Aç", "", PaneCommand.Open, "Enter"));
            if (!item.IsDirectory)
            {
                menu.SecondaryCommands.Add(MenuButton("Birlikte aç…", "", PaneCommand.OpenWith));
            }
            switch (item.Kind)
            {
                case FileKind.Audio:
                    menu.SecondaryCommands.Add(MenuButton("Hızlı çal", "", PaneCommand.Play, "Space"));
                    break;
                case FileKind.Video:
                    menu.SecondaryCommands.Add(MenuButton("Hızlı oynat", "", PaneCommand.Play, "Space"));
                    break;
                case FileKind.Image or FileKind.Pdf or FileKind.Text:
                    menu.SecondaryCommands.Add(MenuButton("Önizle", "", PaneCommand.Preview, "Space"));
                    break;
            }
            if (item.Kind == FileKind.Text)
            {
                menu.SecondaryCommands.Add(MenuButton("Düzenle", "", PaneCommand.Edit, "F4"));
            }
            if (FileKinds.IsRunnable(item))
            {
                menu.SecondaryCommands.Add(MenuButton("Yönetici olarak çalıştır", "", PaneCommand.RunAsAdmin));
            }
            if (item.Kind == FileKind.Archive && !item.IsInArchive && ArchiveService.IsArchive(item.FullPath))
            {
                menu.SecondaryCommands.Add(new AppBarSeparator());
                menu.SecondaryCommands.Add(MenuButton("Buraya çıkar", "\uE8B7", PaneCommand.ExtractHere));
                menu.SecondaryCommands.Add(MenuButton("Karşı panele çıkar", "\uE8DE", PaneCommand.ExtractToOther));
            }
            if (!item.IsInArchive && IsFileSystemFolder)
            {
                menu.SecondaryCommands.Add(MenuButton("ZIP olarak sıkıştır…", "\uF012", PaneCommand.Compress));
                menu.SecondaryCommands.Add(MenuButton("Rafa ekle", "\uE7B8", PaneCommand.AddToShelf));
                menu.SecondaryCommands.Add(MenuButton("Diğer pencereye kopyala", "\uE8A7", PaneCommand.SendToWindow, "Ctrl+Shift+→"));
            }

            menu.SecondaryCommands.Add(new AppBarSeparator());
            menu.SecondaryCommands.Add(MenuButton("Karşı panele kopyala", "", PaneCommand.CopyToOther, "F5"));
            menu.SecondaryCommands.Add(MenuButton("Karşı panele taşı", "", PaneCommand.MoveToOther, "F6"));
            menu.SecondaryCommands.Add(MenuButton("Yolu kopyala", "", PaneCommand.CopyPath, "Ctrl+Shift+C"));
            menu.SecondaryCommands.Add(new AppBarSeparator());
            if (item.IsDirectory)
            {
                menu.SecondaryCommands.Add(MenuButton("Terminalde aç", "", PaneCommand.OpenTerminal));
            }
            menu.SecondaryCommands.Add(MenuButton("Dosya Gezgini'nde göster", "", PaneCommand.ShowInExplorer));
            menu.SecondaryCommands.Add(MenuButton("Özellikler", "", PaneCommand.Properties, "Alt+Enter"));
        }
        else
        {
            menu.PrimaryCommands.Add(MenuButton("Yapıştır", "", PaneCommand.Paste, "Ctrl+V", enabled: canPaste && !IsDriveList));
            menu.PrimaryCommands.Add(MenuButton("Yeni klasör", "\uE8F4", PaneCommand.NewFolder, "F7", enabled: IsFileSystemFolder));
            menu.PrimaryCommands.Add(MenuButton("Yenile", "", PaneCommand.Refresh, "Ctrl+R"));

            if (IsFileSystemFolder)
            {
                var newButton = new AppBarButton { Label = "Yeni", Icon = new FontIcon { Glyph = "\uE710" } };
                var newMenu = new MenuFlyout { Placement = FlyoutPlacementMode.RightEdgeAlignedTop, ShouldConstrainToRootBounds = true };
                NewMenu.Populate(newMenu.Items, entry => _ = Host?.CreateNewAsync(this, entry));
                newMenu.Opened += (_, _) => CursorHelper.ApplyToOpenPopups(XamlRoot);
                newButton.Flyout = newMenu;
                menu.SecondaryCommands.Add(newButton);
                menu.SecondaryCommands.Add(new AppBarSeparator());
                menu.SecondaryCommands.Add(MenuButton("Terminalde aç", "", PaneCommand.OpenTerminal));
                menu.SecondaryCommands.Add(MenuButton("Dosya Gezgini'nde aç", "", PaneCommand.ShowInExplorer));
                menu.SecondaryCommands.Add(MenuButton("Yolu kopyala", "", PaneCommand.CopyPath));
                menu.SecondaryCommands.Add(new AppBarSeparator());
                menu.SecondaryCommands.Add(MenuButton("Klasör özellikleri", "", PaneCommand.Properties));
            }
        }
        return menu;
    }

    private AppBarButton MenuButton(string label, string glyph, PaneCommand command, string? shortcut = null, bool enabled = true)
    {
        var button = new AppBarButton
        {
            Label = label,
            Icon = new FontIcon { Glyph = glyph },
            IsEnabled = enabled,
        };
        if (shortcut is not null) button.KeyboardAcceleratorTextOverride = shortcut;
        ToolTipService.SetToolTip(button, shortcut is null ? label : $"{label} ({shortcut})");
        button.Click += (_, _) => Host?.Execute(command, this);
        return button;
    }

    // ---------------------------------------------------------------- Sürükle-bırak

    private void List_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        HideHover();
        var paths = e.Items.OfType<FileItem>().Where(i => i.IsEntry && !i.IsInArchive).Select(i => i.FullPath).ToList();
        if (paths.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        e.Data.Properties.Add(InternalPathsKey, string.Join('\n', paths));
        e.Data.Properties.Add(InternalSourceKey, _paneId);
        e.Data.RequestedOperation = DataPackageOperation.Move | DataPackageOperation.Copy;

        // Dosya Gezgini ve diğer uygulamalar için gerçek dosyaları geç (talep edildiğinde).
        e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
        {
            var deferral = request.GetDeferral();
            try
            {
                request.SetData(await ClipboardService.LoadStorageItemsAsync(paths));
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    private void List_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult == DataPackageOperation.Move) Refresh();
    }

    private void List_DragOver(object sender, DragEventArgs e)
    {
        bool internalDrag = e.DataView.Properties.TryGetValue(InternalSourceKey, out var source);
        if (IsDriveList || (internalDrag && Equals(source, _paneId)))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            DropHint.Visibility = Visibility.Collapsed;
            return;
        }
        if (!internalDrag && !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        bool copy = e.Modifiers.HasFlag(DragDropModifiers.Control) || IsInArchive;
        e.AcceptedOperation = copy ? DataPackageOperation.Copy : DataPackageOperation.Move;
        var folderName = Path.GetFileName(CurrentPath.TrimEnd('\\'));
        if (folderName.Length == 0) folderName = CurrentPath;
        e.DragUIOverride.Caption = copy ? $"Kopyala → {folderName}" : $"Taşı → {folderName}";
        e.DragUIOverride.IsCaptionVisible = true;

        DropHintText.Text = copy ? "Kopyalamak için bırak" : "Taşımak için bırak · Ctrl: kopyala";
        DropHint.Visibility = Visibility.Visible;
    }

    private void List_DragLeave(object sender, DragEventArgs e) => DropHint.Visibility = Visibility.Collapsed;

    private async void List_Drop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        if (Host is null || IsDriveList) return;

        bool copy = e.Modifiers.HasFlag(DragDropModifiers.Control) || IsInArchive;
        var deferral = e.GetDeferral();
        List<string> paths;
        try
        {
            if (e.DataView.Properties.TryGetValue(InternalPathsKey, out var raw) && raw is string joined)
            {
                paths = joined.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            }
            else if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            }
            else
            {
                return;
            }
            e.AcceptedOperation = copy ? DataPackageOperation.Copy : DataPackageOperation.Move;
        }
        finally
        {
            deferral.Complete();
        }

        if (paths.Count > 0) await Host.DropFilesAsync(this, paths, copy);
    }

    // ---------------------------------------------------------------- Hızlı bakış (hover)

    private void Row_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        var settings = AppSettings.Current;
        if (!settings.HoverPreview) return;
        if ((sender as FrameworkElement)?.DataContext is not FileItem { IsEntry: true, IsInArchive: false } item) return;
        if (!FileKinds.SupportsHover(item.Kind)) return;

        _hoverItem = item;
        _hoverPoint = e.GetCurrentPoint(PaneGrid).Position;
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, settings.HoverDelayMs));
        _hoverTimer.Start();
    }

    private void Row_PointerExited(object sender, PointerRoutedEventArgs e) => HideHover();

    private void HideHover()
    {
        _hoverTimer.Stop();
        _hoverItem = null;
        HoverPopup.IsOpen = false;
    }

    private async void HoverTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        var item = _hoverItem;
        if (item is null) return;

        var image = await FilePreview.GetVisualAsync(item.FullPath, item.Kind, 320);
        if (image is null || !ReferenceEquals(item, _hoverItem)) return;

        HoverImage.Source = image;
        HoverTitle.Text = item.Name;
        HoverMeta.Text = $"{FileKinds.Label(item.Kind)} · {item.SizeText} · {item.ModifiedText}";

        const double cardWidth = 300, cardHeight = 300;
        double x = _hoverPoint.X + 24;
        if (x + cardWidth > PaneGrid.ActualWidth) x = Math.Max(0, _hoverPoint.X - cardWidth - 16);
        double y = Math.Min(_hoverPoint.Y + 12, Math.Max(0, PaneGrid.ActualHeight - cardHeight));
        HoverPopup.HorizontalOffset = x;
        HoverPopup.VerticalOffset = y;
        HoverPopup.IsOpen = true;
    }
}
