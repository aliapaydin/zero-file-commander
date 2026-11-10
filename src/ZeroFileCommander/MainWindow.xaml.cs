using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using ZeroFileCommander.Controls;
using ZeroFileCommander.Services;

namespace ZeroFileCommander;

public sealed partial class MainWindow : Window
{
    private FilePane _active;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1400, 860));

        _active = LeftPane;
        LeftPane.Activated += (_, _) => SetActive(LeftPane);
        RightPane.Activated += (_, _) => SetActive(RightPane);

        LeftPane.Navigate(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        RightPane.Navigate(string.Empty);
        SetActive(LeftPane);
        LeftPane.FocusList();
    }

    private FilePane Other => _active == LeftPane ? RightPane : LeftPane;

    private IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    private void SetActive(FilePane pane)
    {
        _active = pane;
        LeftPane.SetActive(pane == LeftPane);
        RightPane.SetActive(pane == RightPane);
    }

    private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Tab ile paneller arasında geç (yol kutusunda yazarken değil).
        if (e.Key == VirtualKey.Tab && FocusManager.GetFocusedElement(Content.XamlRoot) is not TextBox)
        {
            SetActive(Other);
            _active.FocusList();
            e.Handled = true;
        }
    }

    // ---- Komutlar ----

    private void ViewSelected()
    {
        if (_active.FocusedItem is { } item) _active.Open(item);
    }

    private void EditSelected()
    {
        if (_active.FocusedItem is not { IsDirectory: false } item) return;
        // TODO: dahili düzenleyici (mockup ekran 2). Şimdilik Not Defteri.
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{item.FullPath}\"") { UseShellExecute = true });
    }

    private async Task TransferAsync(bool move)
    {
        var items = _active.SelectedItems;
        if (items.Count == 0) return;
        if (Other.IsDriveList)
        {
            await ShowMessageAsync("Hedef seçilmedi", "Karşı panelde bir klasör açın.");
            return;
        }

        var sources = items.Select(i => i.FullPath).ToList();
        _ = move
            ? await ShellFileOperations.MoveAsync(Hwnd, sources, Other.CurrentPath)
            : await ShellFileOperations.CopyAsync(Hwnd, sources, Other.CurrentPath);

        _active.Refresh();
        Other.Refresh();
    }

    private async Task NewFolderAsync()
    {
        if (_active.IsDriveList) return;

        var box = new TextBox { PlaceholderText = "Klasör adı", Text = "Yeni klasör" };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Yeni klasör",
            Content = box,
            PrimaryButtonText = "Oluştur",
            CloseButtonText = "Vazgeç",
            DefaultButton = ContentDialogButton.Primary,
        };
        box.SelectAll();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var name = box.Text.Trim();
        if (name.Length == 0) return;

        try
        {
            Directory.CreateDirectory(Path.Combine(_active.CurrentPath, name));
            _active.Refresh();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Klasör oluşturulamadı", ex.Message);
        }
    }

    private async Task DeleteAsync()
    {
        var items = _active.SelectedItems;
        if (items.Count == 0) return;

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = items.Count == 1 ? $"\"{items[0].Name}\" silinsin mi?" : $"{items.Count} öğe silinsin mi?",
            Content = "Geri Dönüşüm Kutusu'na gönderilecek.",
            PrimaryButtonText = "Sil",
            CloseButtonText = "Vazgeç",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        await ShellFileOperations.RecycleAsync(Hwnd, items.Select(i => i.FullPath));
        _active.Refresh();
        if (string.Equals(Other.CurrentPath, _active.CurrentPath, StringComparison.OrdinalIgnoreCase)) Other.Refresh();
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "Tamam",
        };
        await dialog.ShowAsync();
    }

    // ---- Klavye kısayolları ----

    private void View_Invoked(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs a) { a.Handled = true; ViewSelected(); }
    private void Edit_Invoked(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs a) { a.Handled = true; EditSelected(); }
    private async void Copy_Invoked(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs a) { a.Handled = true; await TransferAsync(move: false); }
    private async void Move_Invoked(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs a) { a.Handled = true; await TransferAsync(move: true); }
    private async void NewFolder_Invoked(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs a) { a.Handled = true; await NewFolderAsync(); }
    private async void Delete_Invoked(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs a)
    {
        if (FocusManager.GetFocusedElement(Content.XamlRoot) is TextBox) return;
        a.Handled = true;
        await DeleteAsync();
    }
    private void Refresh_Invoked(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs a) { a.Handled = true; _active.Refresh(); Other.Refresh(); }

    // ---- Fonksiyon tuşu çubuğu ----

    private void View_Click(object sender, RoutedEventArgs e) => ViewSelected();
    private void Edit_Click(object sender, RoutedEventArgs e) => EditSelected();
    private async void Copy_Click(object sender, RoutedEventArgs e) => await TransferAsync(move: false);
    private async void Move_Click(object sender, RoutedEventArgs e) => await TransferAsync(move: true);
    private async void NewFolder_Click(object sender, RoutedEventArgs e) => await NewFolderAsync();
    private async void Delete_Click(object sender, RoutedEventArgs e) => await DeleteAsync();
}
