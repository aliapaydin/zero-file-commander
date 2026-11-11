using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZeroFileCommander.Models;
using ZeroFileCommander.Services;

namespace ZeroFileCommander.Views;

/// <summary>Toplu yeniden adlandırma: şablon + bul/değiştir + canlı önizleme.</summary>
public sealed partial class BatchRenameView : UserControl
{
    private readonly IReadOnlyList<FileItem> _items;
    private bool _ready;

    public BatchRenameView() : this([]) { }

    public BatchRenameView(IReadOnlyList<FileItem> items)
    {
        _items = items;
        InitializeComponent();
        Loc.Apply(this); // XAML metinleri seçili dile
        _ready = true;
        Recalculate();
    }

    public List<RenameRow> Rows { get; private set; } = new();

    /// <summary>Uygulanabilir mi (en az bir değişiklik, hiç sorun yok).</summary>
    public bool CanApply => Rows.Any(r => r.IsChanged) && !Rows.Any(r => r.HasProblem);

    public event EventHandler? Changed;

    private RenameOptions ReadOptions() => new(
        NameMask.Text,
        ExtMask.Text,
        FindBox.Text,
        ReplaceBox.Text,
        RegexCheck.IsChecked == true,
        (RenameCase)Math.Max(0, CaseChoice.SelectedIndex),
        SafeInt(CounterStart.Value, 1),
        SafeInt(CounterStep.Value, 1),
        Math.Clamp(SafeInt(CounterDigits.Value, 1), 1, 9));

    private static int SafeInt(double value, int fallback) => double.IsNaN(value) ? fallback : (int)value;

    private void Recalculate()
    {
        if (!_ready) return;
        Rows = BatchRename.Preview(_items, ReadOptions());
        PreviewList.ItemsSource = Rows;
        SummaryText.Text = BatchRename.Summary(Rows);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Option_Changed(object sender, RoutedEventArgs e) => Recalculate();

    private void Number_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args) => Recalculate();

    private void Token_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string token) return;
        int caret = NameMask.SelectionStart;
        NameMask.Text = NameMask.Text.Insert(Math.Clamp(caret, 0, NameMask.Text.Length), token);
        NameMask.SelectionStart = caret + token.Length;
        NameMask.Focus(FocusState.Programmatic);
    }
}
