using System.Collections.ObjectModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;
using ZeroFileCommander.Controls;
using ZeroFileCommander.Models;
using ZeroFileCommander.Services;

namespace ZeroFileCommander.Views;

/// <summary>
/// Ekran kenarında, her zaman üstte duran küçük raf: dosyaları topla,
/// sonra başka bir pencereye/monitöre ya da başka bir uygulamaya sürükle.
/// </summary>
public sealed partial class ShelfWindow : Window
{
    private static ShelfWindow? _instance;

    public ObservableCollection<FileItem> Items { get; } = new();

    public ShelfWindow()
    {
        InitializeComponent();
        Loc.Apply(this); // XAML metinleri seçili dile
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.IsShownInSwitchers = true;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
        }
        RootGrid.RequestedTheme = ThemeHelper.ToElementTheme(AppSettings.Current.Theme);
        PlaceAtEdge();
        Items.CollectionChanged += (_, _) => UpdateCount();
        Closed += (_, _) => _instance = null;
        UpdateCount();
    }

    /// <summary>Rafı açar ya da kapatır.</summary>
    public static void Toggle()
    {
        if (_instance is not null)
        {
            _instance.Close();
            return;
        }
        _instance = new ShelfWindow();
        _instance.Activate();
    }

    /// <summary>Rafa dosya ekler (açık değilse açar).</summary>
    public static void Add(IEnumerable<string> paths)
    {
        if (_instance is null)
        {
            _instance = new ShelfWindow();
            _instance.Activate();
        }
        _instance.AddPaths(paths);
    }

    private void PlaceAtEdge()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        double scale = 1.0;
        try { scale = Math.Max(1.0, GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0); } catch { /* 100% */ }
        int width = (int)(280 * scale), height = (int)(480 * scale);
        AppWindow.MoveAndResize(new RectInt32(area.X + area.Width - width - (int)(12 * scale), area.Y + (area.Height - height) / 2, width, height));
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Items.Any(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase))) continue;
            FileItem? item = null;
            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                item = new FileItem { Name = info.Name, FullPath = info.FullName, IsDirectory = true, Modified = info.LastWriteTime };
            }
            else if (File.Exists(path))
            {
                var info = new FileInfo(path);
                item = new FileItem { Name = info.Name, FullPath = info.FullName, Size = info.Length, Modified = info.LastWriteTime };
            }
            if (item is null) continue;
            item.Icon = ShellIcons.Get(item);
            Items.Add(item);
        }
    }

    private void UpdateCount()
    {
        EmptyHint.Visibility = Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        long bytes = Items.Where(i => !i.IsDirectory).Sum(i => i.Size ?? 0);
        CountText.Text = Items.Count == 0 ? string.Empty : Loc.F("{0} öğe · {1}", Items.Count, FileItem.FormatSize(bytes));
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems) && !e.DataView.Properties.ContainsKey(FilePane.InternalPathsKey))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }
        // Rafa koymak dosyayı taşımaz, yalnızca hatırlar.
        e.AcceptedOperation = DataPackageOperation.Link | DataPackageOperation.Copy;
        e.DragUIOverride.Caption = Loc.T("Rafa koy");
        DropHint.Visibility = Visibility.Visible;
    }

    private void Root_DragLeave(object sender, DragEventArgs e) => DropHint.Visibility = Visibility.Collapsed;

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        var deferral = e.GetDeferral();
        try
        {
            if (e.DataView.Properties.TryGetValue(FilePane.InternalPathsKey, out var raw) && raw is string joined)
            {
                AddPaths(joined.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            }
            else if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                AddPaths(items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)));
            }
            e.AcceptedOperation = DataPackageOperation.Link;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ItemList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var dragged = e.Items.OfType<FileItem>().ToList();
        if (dragged.Count == 0 && Items.Count > 0) dragged = Items.ToList();
        var paths = dragged.Where(i => File.Exists(i.FullPath) || Directory.Exists(i.FullPath)).Select(i => i.FullPath).ToList();
        if (paths.Count == 0)
        {
            e.Cancel = true;
            return;
        }
        e.Data.Properties.Add(FilePane.InternalPathsKey, string.Join('\n', paths));
        e.Data.Properties.Add(FilePane.InternalSourceKey, "shelf");
        e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
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

    private void ItemList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // Taşınan dosyalar artık eski yerinde değil; raftan düş.
        foreach (var item in Items.Where(i => !File.Exists(i.FullPath) && !Directory.Exists(i.FullPath)).ToList())
            Items.Remove(item);
    }

    private void ItemList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Delete) return;
        foreach (var item in ItemList.SelectedItems.OfType<FileItem>().ToList()) Items.Remove(item);
        e.Handled = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => Items.Clear();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
