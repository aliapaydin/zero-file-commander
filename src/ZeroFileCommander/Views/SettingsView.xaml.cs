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

        LoadValues();
        _loading = false;
    }

    private void LoadValues()
    {
        _loading = true;
        ThemeChoice.SelectedIndex = (int)_settings.Theme;
        HoverToggle.IsOn = _settings.HoverPreview;
        GitToggle.IsOn = _settings.ShowGitStatus;
        HoverDelay.Value = _settings.HoverDelayMs;
        HoverDelayText.Text = $"{_settings.HoverDelayMs} ms";
        PinToggle.IsOn = AppLock.IsEnabled;
        PinResumeCheck.IsChecked = _settings.PinOnResume;
        UpdatePinControls();
        QueueToggle.IsOn = _settings.UseTransferQueue;
        ConflictChoice.SelectedIndex = (int)_settings.DefaultConflictPolicy;
        ConflictChoice.IsEnabled = _settings.UseTransferQueue;
        BackgroundToggle.IsOn = _settings.RunInBackground;
        MinimizeToggle.IsOn = _settings.MinimizeToTray;
        DownloadsToggle.IsOn = _settings.DownloadsEnabled;
        FolderBox.Text = _settings.DownloadFolder;
        ClipboardToggle.IsOn = _settings.CaptureClipboardLinks;
        PositionChoice.SelectedIndex = (int)_settings.NotificationPosition;
        DownloadOptions.Visibility = _settings.DownloadsEnabled ? Visibility.Visible : Visibility.Collapsed;

        _loading = false;
    }

    private void ResetLayout_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetLayout();
        ResetStatus.Text = "Yerleşim varsayılana döndü.";
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetAll();
        LoadValues();
        ResetStatus.Text = "Tüm ayarlar varsayılana döndü.";
    }

    private void ThemeChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeChoice.SelectedIndex < 0) return;
        _settings.Theme = (AppTheme)ThemeChoice.SelectedIndex;
    }

    private void GitToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading) _settings.ShowGitStatus = GitToggle.IsOn;
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

    // ---------------------------------------------------------------- PIN

    private enum PinMode { None, Enable, Change, Disable }
    private PinMode _pinMode;
    private bool _pinSuppress;

    private void UpdatePinControls()
    {
        bool enabled = AppLock.IsEnabled;
        PinChange.Visibility = enabled && _pinMode == PinMode.None ? Visibility.Visible : Visibility.Collapsed;
        PinResumeCheck.IsEnabled = enabled;
        PinEditor.Visibility = _pinMode == PinMode.None ? Visibility.Collapsed : Visibility.Visible;
        CurrentPin.Visibility = _pinMode is PinMode.Change or PinMode.Disable ? Visibility.Visible : Visibility.Collapsed;
        NewPin.Visibility = ConfirmPin.Visibility = _pinMode is PinMode.Enable or PinMode.Change ? Visibility.Visible : Visibility.Collapsed;
        PinSave.Content = _pinMode switch
        {
            PinMode.Disable => "Kilidi kaldır",
            PinMode.Change => "PIN'i değiştir",
            _ => "PIN'i kaydet",
        };
    }

    private void StartPinEdit(PinMode mode)
    {
        _pinMode = mode;
        CurrentPin.Password = NewPin.Password = ConfirmPin.Password = string.Empty;
        PinMessage.Text = string.Empty;
        UpdatePinControls();
        (mode == PinMode.Enable ? NewPin : CurrentPin).Focus(FocusState.Programmatic);
    }

    private void SetToggleQuietly(bool on)
    {
        _pinSuppress = true;
        PinToggle.IsOn = on;
        _pinSuppress = false;
    }

    private void PinToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _pinSuppress) return;
        // Anahtar, PIN kaydedilene/doğrulanana kadar asıl durumu değiştirmez.
        StartPinEdit(PinToggle.IsOn ? PinMode.Enable : PinMode.Disable);
    }

    private void PinChange_Click(object sender, RoutedEventArgs e) => StartPinEdit(PinMode.Change);

    private void PinCancel_Click(object sender, RoutedEventArgs e)
    {
        _pinMode = PinMode.None;
        SetToggleQuietly(AppLock.IsEnabled);
        UpdatePinControls();
    }

    private async void PinSave_Click(object sender, RoutedEventArgs e)
    {
        if (_pinMode is PinMode.Change or PinMode.Disable)
        {
            if (AppLock.Cooldown is { } wait)
            {
                PinMessage.Text = $"Çok fazla yanlış deneme. {Math.Ceiling(wait.TotalSeconds)} sn bekleyin.";
                return;
            }
            var current = CurrentPin.Password;
            if (!await Task.Run(() => AppLock.Verify(current)))
            {
                PinMessage.Text = "Mevcut PIN yanlış.";
                CurrentPin.Password = string.Empty;
                return;
            }
        }

        if (_pinMode == PinMode.Disable)
        {
            AppLock.Disable();
        }
        else
        {
            if (!AppLock.IsValidPin(NewPin.Password))
            {
                PinMessage.Text = "PIN tam olarak 4 rakam olmalı.";
                return;
            }
            if (NewPin.Password != ConfirmPin.Password)
            {
                PinMessage.Text = "İki PIN aynı değil.";
                ConfirmPin.Password = string.Empty;
                return;
            }
            var pin = NewPin.Password;
            await AppLock.SetPinAsync(pin);
        }

        _pinMode = PinMode.None;
        SetToggleQuietly(AppLock.IsEnabled);
        UpdatePinControls();
    }

    private void PinResume_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading) _settings.PinOnResume = PinResumeCheck.IsChecked == true;
    }

    private void QueueToggle_Toggled(object sender, RoutedEventArgs e)
    {
        ConflictChoice.IsEnabled = QueueToggle.IsOn;
        if (!_loading) _settings.UseTransferQueue = QueueToggle.IsOn;
    }

    private void ConflictChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ConflictChoice.SelectedIndex < 0) return;
        _settings.DefaultConflictPolicy = (ConflictPolicy)ConflictChoice.SelectedIndex;
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
