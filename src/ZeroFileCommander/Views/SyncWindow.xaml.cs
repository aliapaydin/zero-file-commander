using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZeroFileCommander.Services;

namespace ZeroFileCommander.Views;

/// <summary>İki klasörü karşılaştırıp seçilen yönde kopyalayan pencere (hiçbir şey silmez).</summary>
public sealed partial class SyncWindow : Window
{
    private List<SyncEntry> _entries = new();
    private CancellationTokenSource? _cts;

    /// <summary>Senkronizasyon bitince (panelleri yenilemek için).</summary>
    public event EventHandler? Synced;

    public SyncWindow() : this(string.Empty, string.Empty) { }

    public SyncWindow(string left, string right)
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 760));
        RootGrid.RequestedTheme = ThemeHelper.ToElementTheme(AppSettings.Current.Theme);

        LeftBox.Text = left;
        RightBox.Text = right;
        Closed += (_, _) => _cts?.Cancel();
    }

    private async void Compare_Click(object sender, RoutedEventArgs e) => await CompareAsync();

    private async Task CompareAsync()
    {
        var left = LeftBox.Text.Trim();
        var right = RightBox.Text.Trim();
        if (!Directory.Exists(left) || !Directory.Exists(right))
        {
            SummaryText.Text = "İki klasör de var olmalı.";
            return;
        }

        SetBusy(true, "Karşılaştırılıyor…");
        _cts = new CancellationTokenSource();
        try
        {
            _entries = await FolderSync.CompareAsync(left, right, RecursiveCheck.IsChecked == true, ContentCheck.IsChecked == true, _cts.Token);
            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            SummaryText.Text = "Durduruldu.";
        }
        catch (Exception ex)
        {
            SummaryText.Text = ex.Message;
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private void ApplyFilter()
    {
        bool showSame = ShowSameCheck.IsChecked == true;
        var visible = _entries.Where(e => showSame || e.State != SyncState.Same).ToList();
        ResultList.ItemsSource = visible;
        EmptyText.Text = _entries.Count == 0 ? "Karşılaştır'a bas" : "Fark yok — klasörler eşit";
        EmptyText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        if (_entries.Count == 0) { SummaryText.Text = string.Empty; SyncButton.IsEnabled = false; return; }
        int toRight = _entries.Count(e => e.Action == SyncAction.CopyRight);
        int toLeft = _entries.Count(e => e.Action == SyncAction.CopyLeft);
        int same = _entries.Count(e => e.State == SyncState.Same);
        int different = _entries.Count(e => e.State == SyncState.Different);
        SummaryText.Text = $"{_entries.Count} öğe · {same} aynı · {toRight} sağa, {toLeft} sola kopyalanacak"
            + (different > 0 ? $" · {different} farklı (tarihi aynı, karar senin)" : string.Empty)
            + " · Hiçbir dosya silinmez.";
        SyncButton.IsEnabled = toRight + toLeft > 0;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SyncEntry entry) return;
        entry.CycleAction();
        UpdateSummary();
    }

    private void Swap_Click(object sender, RoutedEventArgs e)
    {
        (LeftBox.Text, RightBox.Text) = (RightBox.Text, LeftBox.Text);
        _entries.Clear();
        ApplyFilter();
    }

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Kopyalanıyor…");
        _cts = new CancellationTokenSource();
        var progress = new Progress<(int Done, int Total, string Current)>(p =>
        {
            SyncProgress.Value = p.Total == 0 ? 100 : p.Done * 100.0 / p.Total;
            SummaryText.Text = p.Current.Length > 0 ? $"{p.Done}/{p.Total} · {p.Current}" : $"{p.Done}/{p.Total}";
        });
        try
        {
            var (done, errors) = await FolderSync.ApplyAsync(LeftBox.Text.Trim(), RightBox.Text.Trim(), _entries, progress, _cts.Token);
            Synced?.Invoke(this, EventArgs.Empty);
            SetBusy(false, null);
            await CompareAsync();
            SummaryText.Text = errors.Count == 0
                ? $"{done} öğe kopyalandı. " + SummaryText.Text
                : $"{done} öğe kopyalandı, {errors.Count} hata: {string.Join(" · ", errors.Take(3))}";
        }
        catch (OperationCanceledException)
        {
            SetBusy(false, "Durduruldu.");
            Synced?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void SetBusy(bool busy, string? message)
    {
        CompareButton.IsEnabled = !busy;
        SyncButton.IsEnabled = !busy && _entries.Any(x => x.Action != SyncAction.None);
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        SyncProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        SyncProgress.IsIndeterminate = busy && message == "Karşılaştırılıyor…";
        if (message is not null) SummaryText.Text = message;
    }
}
