using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using ZeroFileCommander.Services;

namespace ZeroFileCommander.Views;

public enum PaletteGroup
{
    Command,
    Folder,
    File,
}

/// <summary>Komut paletindeki bir satır.</summary>
public sealed class PaletteItem
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Glyph { get; set; } = "";
    public string Shortcut { get; set; } = string.Empty;
    public PaletteGroup Group { get; set; }
    public Action? Run { get; set; }
}

/// <summary>Ctrl+K: komutlara, son klasörlere ve dosyalara klavyeyle ulaşma.</summary>
public sealed partial class CommandPalette : UserControl
{
    private IReadOnlyList<PaletteItem> _all = [];
    private bool _open;

    public ObservableCollection<PaletteItem> Results { get; } = new();

    public event EventHandler? Closed;

    public CommandPalette()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public bool IsOpen => _open;

    public void Open(Func<IReadOnlyList<PaletteItem>> provider)
    {
        _all = provider();
        QueryBox.Text = string.Empty;
        Filter(string.Empty);
        Visibility = Visibility.Visible;
        if (!_open)
        {
            _open = true;
            Overlay.Push();
        }
        DispatcherQueue.TryEnqueue(() => QueryBox.Focus(FocusState.Programmatic));
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;
        Visibility = Visibility.Collapsed;
        Overlay.Pop();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Filter(string query)
    {
        query = query.Trim();
        PaletteGroup? only = null;
        if (query.StartsWith('>')) { only = PaletteGroup.Command; query = query[1..].TrimStart(); }
        else if (query.StartsWith('/')) { only = PaletteGroup.Folder; query = query[1..].TrimStart(); }

        var scored = new List<(PaletteItem Item, int Score)>();
        foreach (var item in _all)
        {
            if (only is not null && item.Group != only) continue;
            if (query.Length == 0)
            {
                // Boşken dosyaları gösterme; komutlar ve son klasörler yeter.
                if (item.Group == PaletteGroup.File) continue;
                scored.Add((item, 0));
                continue;
            }
            int score = Score(item, query);
            if (score > 0) scored.Add((item, score));
        }

        Results.Clear();
        foreach (var (item, _) in scored
                     .OrderByDescending(s => s.Score)
                     .ThenBy(s => s.Item.Group)
                     .Take(60))
        {
            Results.Add(item);
        }
        EmptyText.Visibility = Results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Results.Count > 0) ResultList.SelectedIndex = 0;
    }

    /// <summary>Basit bulanık eşleşme: başta > içinde > harf sırası.</summary>
    private static int Score(PaletteItem item, string query)
    {
        var title = item.Title;
        if (title.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)) return 1000 - title.Length;
        int index = title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
        if (index >= 0) return 800 - index * 4 - title.Length;
        if (IsSubsequence(title, query)) return 500 - title.Length;
        if (item.Subtitle.Contains(query, StringComparison.CurrentCultureIgnoreCase)) return 300;
        return 0;
    }

    private static bool IsSubsequence(string text, string query)
    {
        int q = 0;
        foreach (char c in text)
        {
            if (q < query.Length && char.ToLowerInvariant(c) == char.ToLowerInvariant(query[q])) q++;
        }
        return q == query.Length;
    }

    private void Execute(PaletteItem? item)
    {
        if (item is null) return;
        Close();
        item.Run?.Invoke();
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => Filter(QueryBox.Text);

    private void QueryBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down:
                if (Results.Count > 0) ResultList.SelectedIndex = Math.Min(ResultList.SelectedIndex + 1, Results.Count - 1);
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                if (Results.Count > 0) ResultList.SelectedIndex = Math.Max(ResultList.SelectedIndex - 1, 0);
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                Execute(ResultList.SelectedItem as PaletteItem);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void ResultList_ItemClick(object sender, ItemClickEventArgs e) => Execute(e.ClickedItem as PaletteItem);

    private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e) => Close();

    private void Card_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;
}
