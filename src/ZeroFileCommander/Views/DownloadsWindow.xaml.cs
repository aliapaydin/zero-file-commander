using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.System;
using ZeroFileCommander.Services;

namespace ZeroFileCommander.Views;

/// <summary>Ayrı indirme penceresi.</summary>
public sealed partial class DownloadsWindow : Window
{
    public DownloadManager Manager => DownloadManager.Current;

    public DownloadsWindow()
    {
        InitializeComponent();
        Loc.Apply(this); // XAML metinleri seçili dile
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        var placement = new WindowPlacement(AppWindow, DispatcherQueue,
            () => AppSettings.Current.DownloadsWindowBounds, value => AppSettings.Current.DownloadsWindowBounds = value);
        placement.Restore(new Windows.Graphics.SizeInt32(760, 620));
        AppWindow.Closing += (_, _) => placement.Save();

        RootGrid.RequestedTheme = ThemeHelper.ToElementTheme(AppSettings.Current.Theme);
        FolderText.Text = AppSettings.Current.DownloadFolder;

        Manager.Items.CollectionChanged += Items_CollectionChanged;
        Manager.ProgressChanged += Manager_ProgressChanged;
        Closed += (_, _) =>
        {
            Manager.Items.CollectionChanged -= Items_CollectionChanged;
            Manager.ProgressChanged -= Manager_ProgressChanged;
        };
        UpdateSummary();
    }

    public void ApplyTheme() => RootGrid.RequestedTheme = ThemeHelper.ToElementTheme(AppSettings.Current.Theme);

    public void FocusUrl(string? url = null)
    {
        if (url is not null) UrlBox.Text = url;
        UrlBox.Focus(FocusState.Programmatic);
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateSummary();

    private void Manager_ProgressChanged(object? sender, EventArgs e) => UpdateSummary();

    private void UpdateSummary()
    {
        int total = Manager.Items.Count;
        int active = Manager.ActiveCount;
        EmptyText.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        SummaryText.Text = total == 0 ? string.Empty : active > 0 ? Loc.F("{0} indirme sürüyor · toplam {1}", active, total) : Loc.F("{0} indirme", total);
    }

    private void StartFromBox()
    {
        var url = UrlBox.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            UrlBox.Header = Loc.T("Geçerli bir http/https bağlantısı gir");
            return;
        }
        UrlBox.Header = null;
        Manager.Start(url, AppSettings.Current.DownloadFolder);
        UrlBox.Text = string.Empty;
    }

    private void Download_Click(object sender, RoutedEventArgs e) => StartFromBox();

    private void UrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        StartFromBox();
        e.Handled = true;
    }

    private async void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        AppSettings.Current.DownloadFolder = folder.Path;
        FolderText.Text = folder.Path;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.Current.DownloadFolder);
        ShellActions.Open(AppSettings.Current.DownloadFolder);
    }

    private static DownloadItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as DownloadItem;

    private void Pause_Click(object sender, RoutedEventArgs e) { if (ItemOf(sender) is { } i) Manager.Pause(i); }
    private void Resume_Click(object sender, RoutedEventArgs e) { if (ItemOf(sender) is { } i) Manager.Resume(i); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { if (ItemOf(sender) is { } i) Manager.Cancel(i); }
    private void Remove_Click(object sender, RoutedEventArgs e) { if (ItemOf(sender) is { } i) Manager.Remove(i); }
    private void Open_Click(object sender, RoutedEventArgs e) { if (ItemOf(sender) is { FilePath.Length: > 0 } i) ShellActions.Open(i.FilePath); }
    private void ShowInFolder_Click(object sender, RoutedEventArgs e) { if (ItemOf(sender) is { FilePath.Length: > 0 } i) ShellActions.ShowInExplorer(i.FilePath); }
    private void ClearFinished_Click(object sender, RoutedEventArgs e) => Manager.ClearFinished();
}
