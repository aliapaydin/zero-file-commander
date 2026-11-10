using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Storage.Pickers;
using ZeroFileCommander.Services;

namespace ZeroFileCommander.Views;

/// <summary>Ayarlar diyaloğunun içeriği. Her değişiklik anında kaydedilir.</summary>
public sealed partial class SettingsView : UserControl
{
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly IntPtr _ownerHwnd;
    private bool _loading = true;

    public SettingsView() : this(IntPtr.Zero) { }

    public SettingsView(IntPtr ownerHwnd)
    {
        _ownerHwnd = ownerHwnd;
        InitializeComponent();

        ThemeChoice.SelectedIndex = (int)_settings.Theme;
        HoverToggle.IsOn = _settings.HoverPreview;
        HoverDelay.Value = _settings.HoverDelayMs;
        HoverDelayText.Text = $"{_settings.HoverDelayMs} ms";
        BackgroundToggle.IsOn = _settings.RunInBackground;
        MinimizeToggle.IsOn = _settings.MinimizeToTray;
        DownloadsToggle.IsOn = _settings.DownloadsEnabled;
        FolderBox.Text = _settings.DownloadFolder;
        ClipboardToggle.IsOn = _settings.CaptureClipboardLinks;
        PositionChoice.SelectedIndex = (int)_settings.NotificationPosition;
        DownloadOptions.Visibility = _settings.DownloadsEnabled ? Visibility.Visible : Visibility.Collapsed;

        _loading = false;
    }

    private void ThemeChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeChoice.SelectedIndex < 0) return;
        _settings.Theme = (AppTheme)ThemeChoice.SelectedIndex;
    }

    private void HoverToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _settings.HoverPreview = HoverToggle.IsOn;
    }

    private void HoverDelay_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (HoverDelayText is null) return;
        HoverDelayText.Text = $"{(int)e.NewValue} ms";
        if (!_loading) _settings.HoverDelayMs = (int)e.NewValue;
    }

    private void BackgroundToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _settings.RunInBackground = BackgroundToggle.IsOn;
    }

    private void MinimizeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _settings.MinimizeToTray = MinimizeToggle.IsOn;
    }

    private void DownloadsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        DownloadOptions.Visibility = DownloadsToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        if (!_loading) _settings.DownloadsEnabled = DownloadsToggle.IsOn;
    }

    private void ClipboardToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _settings.CaptureClipboardLinks = ClipboardToggle.IsOn;
    }

    private void PositionChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PositionChoice.SelectedIndex < 0) return;
        _settings.NotificationPosition = (NotificationPosition)PositionChoice.SelectedIndex;
    }

    private async void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _ownerHwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        _settings.DownloadFolder = folder.Path;
        FolderBox.Text = folder.Path;
    }

    private void TestNotification_Click(object sender, RoutedEventArgs e) =>
        Notifier.Show("Bildirimler burada görünecek", "Konumu bu ayardan değiştirebilirsin.", "");
}
