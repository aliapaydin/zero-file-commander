using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZeroFileCommander.Services;

namespace ZeroFileCommander.Views;

/// <summary>Kopyalama/taşıma kuyruğu penceresi.</summary>
public sealed partial class TransfersWindow : Window
{
    private bool _loading = true;

    public TransferManager Manager => TransferManager.Current;

    public TransfersWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarArea);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(820, 620));
        RootGrid.RequestedTheme = ThemeHelper.ToElementTheme(AppSettings.Current.Theme);

        PolicyChoice.SelectedIndex = (int)AppSettings.Current.DefaultConflictPolicy;
        _loading = false;

        Manager.Jobs.CollectionChanged += Jobs_CollectionChanged;
        Manager.ProgressChanged += Manager_ProgressChanged;
        Closed += (_, _) =>
        {
            Manager.Jobs.CollectionChanged -= Jobs_CollectionChanged;
            Manager.ProgressChanged -= Manager_ProgressChanged;
        };
        UpdateSummary();
    }

    public void ApplyTheme() => RootGrid.RequestedTheme = ThemeHelper.ToElementTheme(AppSettings.Current.Theme);

    private void Jobs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateSummary();

    private void Manager_ProgressChanged(object? sender, EventArgs e) => UpdateSummary();

    private void UpdateSummary()
    {
        int total = Manager.Jobs.Count;
        int active = Manager.ActiveCount;
        EmptyText.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        SummaryText.Text = total == 0 ? string.Empty : active > 0 ? $"{active} iş sürüyor · toplam {total}" : $"{total} iş tamamlandı";
    }

    private void PolicyChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PolicyChoice.SelectedIndex < 0) return;
        AppSettings.Current.DefaultConflictPolicy = (ConflictPolicy)PolicyChoice.SelectedIndex;
    }

    private static TransferJob? JobOf(object sender) => (sender as FrameworkElement)?.DataContext as TransferJob;

    private void Pause_Click(object sender, RoutedEventArgs e) { if (JobOf(sender) is { } j) Manager.Pause(j); }
    private void Resume_Click(object sender, RoutedEventArgs e) { if (JobOf(sender) is { } j) Manager.Resume(j); }
    private void Cancel_Click(object sender, RoutedEventArgs e) { if (JobOf(sender) is { } j) Manager.Cancel(j); }
    private void Remove_Click(object sender, RoutedEventArgs e) { if (JobOf(sender) is { } j) Manager.Remove(j); }
    private void ClearFinished_Click(object sender, RoutedEventArgs e) => Manager.ClearFinished();

    private void Resolve_Click(object sender, RoutedEventArgs e)
    {
        if (JobOf(sender) is not { } job || (sender as FrameworkElement)?.Tag is not string tag) return;
        if (int.TryParse(tag, out var value)) Manager.ResolveConflicts(job, (ConflictPolicy)value);
    }
}
