using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.Foundation;
using Windows.System;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using ZeroFileCommander.Models;
using ZeroFileCommander.Services;

namespace ZeroFileCommander.Controls;

/// <summary>Tek bir dosya paneli. İki tanesi yan yana ana pencereyi oluşturur.</summary>
public sealed partial class FilePane : UserControl
{
    private const string InternalPathsKey = "ZFC.Paths";
    private const string InternalSourceKey = "ZFC.Source";

    private static readonly EnumerationOptions Enumeration = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    private readonly string _paneId = Guid.NewGuid().ToString("N");
    private readonly DispatcherQueueTimer _hoverTimer;
    private FileItem? _hoverItem;
    private Point _hoverPoint;

    public ObservableCollection<FileItem> Items { get; } = new();

    /// <summary>Boş string = "Bu bilgisayar" (sürücü listesi).</summary>
    public string CurrentPath { get; private set; } = string.Empty;

    public bool IsDriveList => CurrentPath.Length == 0;

    public IPaneHost? Host { get; set; }

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
    }

    /// <summary>Seçili gerçek öğeler ("..", sürücüler hariç).</summary>
    public IReadOnlyList<FileItem> SelectedItems =>
        FileList.SelectedItems.Cast<FileItem>().Where(i => i.IsEntry).ToList();

    /// <summary>İmlecin bulunduğu öğe.</summary>
    public FileItem? FocusedItem => FileList.SelectedItem as FileItem;

    public void FocusList() => FileList.Focus(FocusState.Programmatic);

    public void SetActive(bool active)
    {
        var key = active ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush";
        RootBorder.BorderBrush = (Brush)Application.Current.Resources[key];
        RootBorder.BorderThickness = new Thickness(active ? 1.5 : 1);
    }

    public void ShowStatus(string text) => StatusText.Text = text;

    // ---------------------------------------------------------------- Gezinme

    public void Navigate(string? path)
    {
        var previous = CurrentPath;
        var newItems = new List<FileItem>();

        if (string.IsNullOrEmpty(path))
        {
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
                newItems.Add(new FileItem { Name = label, FullPath = drive.RootDirectory.FullName, IsDirectory = true, IsDrive = true });
            }
            CurrentPath = string.Empty;
            PathBox.Text = "Bu bilgisayar";
        }
        else
        {
            try
            {
                var dir = new DirectoryInfo(path);
                newItems.Add(new FileItem { Name = "..", FullPath = dir.Parent?.FullName ?? string.Empty, IsDirectory = true, IsParent = true });

                foreach (var d in dir.EnumerateDirectories("*", Enumeration).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    newItems.Add(new FileItem { Name = d.Name, FullPath = d.FullName, IsDirectory = true, Modified = d.LastWriteTime });
                }
                foreach (var f in dir.EnumerateFiles("*", Enumeration).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    newItems.Add(new FileItem { Name = f.Name, FullPath = f.FullName, Size = f.Length, Modified = f.LastWriteTime });
                }
                CurrentPath = dir.FullName;
                PathBox.Text = CurrentPath;
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
                PathBox.Text = previous.Length == 0 ? "Bu bilgisayar" : previous;
                return;
            }
        }

        // Yenilemede seçimi korumak için önceki seçili yolları hatırla.
        var keep = new HashSet<string>(FileList.SelectedItems.Cast<FileItem>().Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);
        bool sameFolder = string.Equals(previous, CurrentPath, StringComparison.OrdinalIgnoreCase);

        Items.Clear();
        foreach (var item in newItems) Items.Add(item);

        if (sameFolder && keep.Count > 0)
        {
            foreach (var item in Items.Where(i => keep.Contains(i.FullPath))) FileList.SelectedItems.Add(item);
        }
        if (FileList.SelectedItems.Count == 0)
        {
            // Üst klasöre çıkınca geldiğimiz klasörü seçili getir (Total Commander davranışı).
            var reselect = Items.FirstOrDefault(i => !i.IsParent && string.Equals(i.FullPath.TrimEnd('\\'), previous.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            FileList.SelectedItem = reselect ?? Items.FirstOrDefault();
        }
        if (FileList.SelectedItem is not null) FileList.ScrollIntoView(FileList.SelectedItem);

        UpdateStatus();
        SelectionChangedEx?.Invoke(this, EventArgs.Empty);
    }

    public void Refresh() => Navigate(CurrentPath);

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

    public void SelectAll(bool invert)
    {
        var current = new HashSet<FileItem>(FileList.SelectedItems.Cast<FileItem>());
        FileList.SelectedItems.Clear();
        foreach (var item in Items.Where(i => i.IsEntry))
        {
            if (!invert || !current.Contains(item)) FileList.SelectedItems.Add(item);
        }
    }

    public void Open(FileItem item)
    {
        if (item.IsDirectory)
            Navigate(item.FullPath);
        else
            ShellActions.Open(item.FullPath);
    }

    private void UpdateStatus()
    {
        var selected = SelectedItems;
        if (selected.Count > 1)
        {
            long bytes = selected.Where(i => !i.IsDirectory).Sum(i => i.Size ?? 0);
            StatusText.Text = $"{selected.Count} öğe seçili · {FileItem.FormatSize(bytes)}";
            return;
        }
        int dirs = Items.Count(i => i.IsDirectory && !i.IsParent);
        int files = Items.Count(i => !i.IsDirectory);
        StatusText.Text = IsDriveList ? $"{dirs} sürücü" : $"{dirs} klasör, {files} dosya";
    }

    // ---------------------------------------------------------------- Olaylar

    private void OnGotFocus(object sender, RoutedEventArgs e) => Activated?.Invoke(this, EventArgs.Empty);

    private void UpButton_Click(object sender, RoutedEventArgs e) => GoUp();

    private void PathBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        var text = PathBox.Text.Trim();
        Navigate(text == "Bu bilgisayar" ? string.Empty : Environment.ExpandEnvironmentVariables(text));
        FocusList();
        e.Handled = true;
    }

    private void List_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FocusedItem is { } item) Open(item);
    }

    private void List_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter when FocusedItem is { } item:
                Open(item);
                e.Handled = true;
                break;
            case VirtualKey.Back:
                GoUp();
                e.Handled = true;
                break;
            case VirtualKey.Space when FocusedItem is { IsEntry: true }:
                Host?.Execute(PaneCommand.Preview, this);
                e.Handled = true;
                break;
        }
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateStatus();
        SelectionChangedEx?.Invoke(this, EventArgs.Empty);
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
        menu.ShowAt(FileList, new FlyoutShowOptions
        {
            Position = e.GetPosition(FileList),
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
            menu.PrimaryCommands.Add(MenuButton("Sil", "", PaneCommand.Delete, "Del"));

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
            menu.PrimaryCommands.Add(MenuButton("Yeni klasör", "", PaneCommand.NewFolder, "F7", enabled: !IsDriveList));
            menu.PrimaryCommands.Add(MenuButton("Yenile", "", PaneCommand.Refresh, "Ctrl+R"));

            if (!IsDriveList)
            {
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
        var paths = e.Items.OfType<FileItem>().Where(i => i.IsEntry).Select(i => i.FullPath).ToList();
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

        bool copy = e.Modifiers.HasFlag(DragDropModifiers.Control);
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

        bool copy = e.Modifiers.HasFlag(DragDropModifiers.Control);
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
        if ((sender as FrameworkElement)?.DataContext is not FileItem { IsEntry: true } item) return;
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
