using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Storage;
using ZeroFileCommander.Models;
using ZeroFileCommander.Services;

namespace ZeroFileCommander.Controls;

/// <summary>
/// Sağdaki önizleme paneli: görsel, PDF (sayfa sayfa), metin/kod, ses, video ve
/// Windows önizleme işleyicisi olan her şey (Word, Excel, PowerPoint…).
/// </summary>
public sealed partial class PreviewPane : UserControl
{
    private int _version;
    private FileItem? _current;
    private PdfDocument? _pdf;
    private readonly List<Border> _pdfPages = new();
    private readonly HashSet<int> _rendering = new();
    private double _renderedWidth;
    private PreviewHandlerHost? _handlerHost;
    private bool _handlerActive;

    public event EventHandler? CloseRequested;

    public PreviewPane()
    {
        InitializeComponent();
        HandlerSurface.LayoutUpdated += (_, _) => PositionHandler();
        Overlay.Changed += (_, _) => UpdateHandlerVisibility();
        Unloaded += (_, _) => _handlerHost?.Dispose();
    }

    /// <summary>Win32 önizleyicilerinin yerleşeceği ana pencere.</summary>
    public IntPtr HostWindow { get; set; }

    public FileItem? Current => _current;

    /// <summary>Gösterilen öğeyi değiştirir. <paramref name="autoPlay"/> ses/videoyu hemen başlatır.</summary>
    public async Task ShowAsync(FileItem? item, bool autoPlay = false)
    {
        // Aynı medya zaten açıksa sadece oynat.
        if (item is not null && ReferenceEquals(item, _current) && Player.Visibility == Visibility.Visible)
        {
            if (autoPlay) Player.MediaPlayer?.Play();
            return;
        }

        int version = ++_version;
        _current = item;
        Reset();

        if (item is null || !item.IsEntry)
        {
            ShowPlaceholder("", "Bir dosya seç");
            NameText.Text = KindText.Text = SizeText.Text = DateText.Text = ExtraText.Text = string.Empty;
            return;
        }

        NameText.Text = item.Name;
        KindText.Text = item.IsDirectory ? "Klasör" : $"{FileKinds.Label(item.Kind)} · {item.Extension}";
        SizeText.Text = item.IsDirectory ? "—" : item.SizeText;
        DateText.Text = item.ModifiedText;
        ExtraText.Text = string.Empty;
        UpdateVsCodeButton(item);

        // Yalnızca çevrimiçi bulut dosyası: seçmek dosyayı indirmesin, kullanıcı istesin.
        if (item.IsOnlineOnly && !string.Equals(_hydrateAllowed, item.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            ShowPlaceholder("\uE753", "Bu dosya yalnızca bulutta. Önizlemek onu bu bilgisayara indirir.");
            PlaceholderAction.Content = "İndir ve önizle";
            PlaceholderAction.Visibility = Visibility.Visible;
            return;
        }

        LoadingRing.IsActive = true;
        try
        {
            string extension = Path.GetExtension(item.FullPath);
            if (!item.IsDirectory && TabularPreview.IsTabular(extension))
                await ShowTableAsync(item, version);
            else if (PowerBiInspector.CanInspect(item.FullPath, item.IsDirectory))
                await ShowMetaAsync(item, version);
            else if (!item.IsDirectory && extension.Equals(".ipynb", StringComparison.OrdinalIgnoreCase))
                await ShowNotebookAsync(item, version);
            else if (!item.IsDirectory && item.Kind is FileKind.Other && SyntaxHighlighter.IsSupported(item.Name))
            {
                var code = await FilePreview.ReadTextHeadAsync(item.FullPath);
                if (version != _version) return;
                if (code is null) ShowPlaceholder(item.Glyph, "Metin okunamadı");
                else ShowCode(code, SyntaxHighlighter.LanguageFor(item.Name));
            }
            else switch (item.Kind)
            {
                case FileKind.Folder:
                    await ShowFolderAsync(item, version);
                    break;

                case FileKind.Image:
                    var image = await FilePreview.GetFullImageAsync(item.FullPath, 1200);
                    if (version != _version) return;
                    if (image is null) { ShowPlaceholder(item.Glyph, "Önizleme yok"); break; }
                    ShowImage(image);
                    if (image.PixelWidth > 0) ExtraText.Text = $"{image.PixelWidth} × {image.PixelHeight}";
                    break;

                case FileKind.Pdf:
                    var doc = await FilePreview.LoadPdfAsync(item.FullPath);
                    if (version != _version) return;
                    if (doc is null || doc.PageCount == 0)
                    {
                        if (!TryOpenHandler(item)) ShowPlaceholder(item.Glyph, "PDF açılamadı");
                        break;
                    }
                    _pdf = doc;
                    await LoadPdfPagesAsync(version);
                    break;

                case FileKind.Audio:
                case FileKind.Video:
                    var file = await StorageFile.GetFileFromPathAsync(item.FullPath);
                    if (version != _version) return;
                    if (item.Kind == FileKind.Audio)
                    {
                        Player.PosterSource = await FilePreview.GetVisualAsync(item.FullPath, item.Kind, 600);
                        if (version != _version) return;
                    }
                    Player.Source = MediaSource.CreateFromStorageFile(file);
                    Player.Visibility = Visibility.Visible;
                    ExtraText.Text = autoPlay ? "Çalıyor" : "Space ile çal";
                    if (autoPlay) Player.MediaPlayer?.Play();
                    break;

                case FileKind.Text:
                    var text = await FilePreview.ReadTextHeadAsync(item.FullPath);
                    if (version != _version) return;
                    if (text is null) { ShowPlaceholder(item.Glyph, "Metin okunamadı"); break; }
                    ShowCode(text, SyntaxHighlighter.LanguageFor(item.Name));
                    break;

                default:
                    // Office ve önizleyicisi kayıtlı diğer türler
                    if (TryOpenHandler(item)) break;

                    var visual = await FilePreview.GetVisualAsync(item.FullPath, item.Kind, 512);
                    if (version != _version) return;
                    if (visual is not null)
                        ShowImage(visual);
                    else
                        ShowPlaceholder(item.Glyph, item.Kind == FileKind.Office
                            ? "Bu belge için Windows önizleyicisi bulunamadı (Office kurulu mu?)"
                            : "Bu tür için önizleme yok");
                    break;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("PreviewPane " + item.Extension, ex);
            if (version == _version) ShowPlaceholder(item.Glyph, "Önizleme yüklenemedi");
        }
        finally
        {
            if (version == _version) LoadingRing.IsActive = false;
        }
    }

    public void Play() => Player.MediaPlayer?.Play();

    /// <summary>Çalan medyayı durdurur, açık belgeleri bırakır (dosya kilitlenmesin).</summary>
    public void Stop()
    {
        try
        {
            Player.MediaPlayer?.Pause();
        }
        catch
        {
            // oynatıcı henüz oluşmamış olabilir
        }
        Player.Source = null;
        Player.PosterSource = null;
        CloseHandler();
        _pdf = null;
    }

    // ---------------------------------------------------------------- Klasör içeriği

    private const int FolderPreviewMax = 80;

    private async Task ShowFolderAsync(FileItem folder, int version)
    {
        var countTask = FilePreview.CountAsync(folder.FullPath);
        var entries = await FilePreview.ListFolderAsync(folder.FullPath, FolderPreviewMax);
        var (folders, files, capped) = await countTask;
        if (version != _version) return;

        if (entries.Count == 0)
        {
            ShowPlaceholder("\uE8B7", "Klasör boş");
            return;
        }

        int total = folders + files;
        FolderSummary.Text = capped
            ? "10.000+ öğe"
            : total > entries.Count
                ? $"{folders} klasör, {files} dosya · ilk {entries.Count} gösteriliyor"
                : $"{folders} klasör, {files} dosya";
        ExtraText.Text = $"{folders} klasör, {files} dosya";

        FolderGrid.Children.Clear();
        var thumbnails = new List<(FileItem Entry, Image Target)>();
        foreach (var entry in entries)
        {
            var tile = new StackPanel { Spacing = 6, Padding = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Center };
            var iconHost = new Grid { Width = 56, Height = 48, HorizontalAlignment = HorizontalAlignment.Center };
            iconHost.Children.Add(new FontIcon
            {
                Glyph = entry.Glyph,
                FontSize = 32,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            });
            if (entry.Kind is FileKind.Image or FileKind.Video or FileKind.Pdf)
            {
                var image = new Image { Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
                var frame = new Border { CornerRadius = new CornerRadius(6), Child = image };
                iconHost.Children.Add(frame);
                thumbnails.Add((entry, image));
            }
            tile.Children.Add(iconHost);
            tile.Children.Add(new TextBlock
            {
                Text = entry.Name,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2,
                MaxWidth = 76,
            });
            FolderGrid.Children.Add(tile);
        }
        FolderScroller.Visibility = Visibility.Visible;
        FolderScroller.ChangeView(null, 0, null, disableAnimation: true);

        // Küçük resimler sırayla gelsin (ilk 40).
        foreach (var (entry, target) in thumbnails.Take(40))
        {
            var bitmap = await FilePreview.GetVisualAsync(entry.FullPath, entry.Kind, 96);
            if (version != _version) return;
            if (bitmap is not null) target.Source = bitmap;
        }
    }

    // ---------------------------------------------------------------- PDF (sayfalar alt alta)

    private async Task LoadPdfPagesAsync(int version)
    {
        if (_pdf is null) return;
        PageStack.Children.Clear();
        _pdfPages.Clear();
        _rendering.Clear();

        uint count = Math.Min(_pdf.PageCount, 500u);
        for (uint i = 0; i < count; i++)
        {
            double ratio = 1.414;
            try
            {
                using var page = _pdf.GetPage(i);
                if (page.Size.Width > 0) ratio = page.Size.Height / page.Size.Width;
            }
            catch
            {
                // boyutu okunamayan sayfa A4 oranında yer tutar
            }
            var holder = new Border
            {
                Tag = ratio,
                CornerRadius = new CornerRadius(4),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                Child = new Image { Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform },
            };
            PageStack.Children.Add(holder);
            _pdfPages.Add(holder);
        }

        ImageScroller.Visibility = Visibility.Visible;
        PageBar.Visibility = _pdf.PageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        ExtraText.Text = _pdf.PageCount > count ? $"{_pdf.PageCount} sayfa (ilk {count} gösteriliyor)" : $"{_pdf.PageCount} sayfa";
        ImageScroller.UpdateLayout();
        UpdatePageHeights();
        await UpdateVisiblePagesAsync(version);
    }

    private double PageWidth => Math.Max(60, ImageScroller.ViewportWidth - 12);

    private void UpdatePageHeights()
    {
        double width = PageWidth;
        foreach (var page in _pdfPages) page.Height = width * (double)page.Tag;
    }

    /// <summary>Görünen sayfaları çizer, uzaktakileri bellekten bırakır.</summary>
    private async Task UpdateVisiblePagesAsync(int version)
    {
        if (_pdf is null || _pdfPages.Count == 0) return;

        double top = ImageScroller.VerticalOffset;
        double viewport = Math.Max(1, ImageScroller.ViewportHeight);
        double scale = XamlRoot?.RasterizationScale ?? 1;
        uint renderWidth = (uint)Math.Clamp(PageWidth * scale, 300, 2000);
        if (Math.Abs(renderWidth - _renderedWidth) > 120)
        {
            // Genişlik çok değişti; netlik için yeniden çiz.
            foreach (var page in _pdfPages) ((Image)page.Child).Source = null;
            _renderedWidth = renderWidth;
        }

        double y = 6;
        int current = 0;
        var toRender = new List<int>();
        for (int i = 0; i < _pdfPages.Count; i++)
        {
            double height = _pdfPages[i].Height + 8;
            bool near = y + height > top - viewport && y < top + 2 * viewport;
            bool far = y + height < top - 3 * viewport || y > top + 4 * viewport;
            var image = (Image)_pdfPages[i].Child;
            if (near && image.Source is null && !_rendering.Contains(i)) toRender.Add(i);
            else if (far && image.Source is not null) image.Source = null;
            if (y < top + viewport / 3) current = i;
            y += height;
        }
        PageText.Text = $"Sayfa {current + 1} / {_pdf.PageCount}";

        foreach (int i in toRender)
        {
            _rendering.Add(i);
            var bitmap = await FilePreview.RenderPageAsync(_pdf, (uint)i, renderWidth);
            _rendering.Remove(i);
            if (version != _version || _pdf is null || i >= _pdfPages.Count) return;
            ((Image)_pdfPages[i].Child).Source = bitmap;
        }
    }

    private async void ImageScroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_pdf is not null) await UpdateVisiblePagesAsync(_version);
    }

    private async void ImageScroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_pdf is null || Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 1) return;
        UpdatePageHeights();
        await UpdateVisiblePagesAsync(_version);
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e) => ScrollToPage(-1);

    private void NextPage_Click(object sender, RoutedEventArgs e) => ScrollToPage(+1);

    private void ScrollToPage(int delta)
    {
        if (_pdfPages.Count == 0) return;

        // Sayfaların başlangıç konumları
        var offsets = new List<double>(_pdfPages.Count);
        double y = 6;
        foreach (var page in _pdfPages)
        {
            offsets.Add(y);
            y += page.Height + 8;
        }

        // Görünümün üst kenarının (biraz altının) düştüğü sayfa = şu anki sayfa
        double probe = ImageScroller.VerticalOffset + 12;
        int current = 0;
        for (int i = 0; i < offsets.Count; i++)
        {
            if (offsets[i] <= probe) current = i;
            else break;
        }

        int target = Math.Clamp(current + delta, 0, _pdfPages.Count - 1);
        ImageScroller.ChangeView(null, offsets[target], null, disableAnimation: true);
    }

    // ---------------------------------------------------------------- Windows önizleyicisi

    private bool TryOpenHandler(FileItem item)
    {
        if (HostWindow == IntPtr.Zero) return false;
        var clsid = PreviewHandlerHost.FindHandler(Path.GetExtension(item.FullPath));
        if (clsid is null) return false;

        HandlerSurface.Visibility = Visibility.Visible;
        HandlerSurface.UpdateLayout();
        var (x, y, w, h) = SurfaceBounds();
        _handlerHost ??= new PreviewHandlerHost(HostWindow);
        _handlerActive = _handlerHost.Open(item.FullPath, clsid.Value, x, y, w, h);
        if (!_handlerActive)
        {
            HandlerSurface.Visibility = Visibility.Collapsed;
            return false;
        }
        ExtraText.Text = "Windows önizleyicisi · kaydırarak sayfalar arasında gez";
        UpdateHandlerVisibility();
        return true;
    }

    private void CloseHandler()
    {
        if (!_handlerActive) return;
        _handlerActive = false;
        _handlerHost?.Close();
    }

    private void PositionHandler()
    {
        if (!_handlerActive || _handlerHost is null) return;
        var (x, y, w, h) = SurfaceBounds();
        _handlerHost.SetBounds(x, y, w, h);
    }

    private void UpdateHandlerVisibility()
    {
        if (_handlerHost is null) return;
        bool visible = _handlerActive && !Overlay.IsOpen && Visibility == Visibility.Visible && HandlerSurface.Visibility == Visibility.Visible;
        _handlerHost.SetVisible(visible);
    }

    private (int X, int Y, int Width, int Height) SurfaceBounds()
    {
        if (XamlRoot is null) return (0, 0, 1, 1);
        double scale = XamlRoot.RasterizationScale;
        var origin = HandlerSurface.TransformToVisual(null).TransformPoint(new Point(0, 0));
        return (
            (int)Math.Round(origin.X * scale),
            (int)Math.Round(origin.Y * scale),
            Math.Max(1, (int)Math.Round(HandlerSurface.ActualWidth * scale)),
            Math.Max(1, (int)Math.Round(HandlerSurface.ActualHeight * scale)));
    }

    /// <summary>Panel gizlenip gösterildiğinde ana pencere çağırır.</summary>
    public void OnVisibilityChanged() => UpdateHandlerVisibility();

    // ---------------------------------------------------------------- Yardımcılar

    private void ShowImage(Microsoft.UI.Xaml.Media.ImageSource source)
    {
        PageStack.Children.Clear();
        var image = new Image
        {
            Source = source,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        // Küçük görselleri büyütüp bulanıklaştırma; büyükleri genişliğe sığdır.
        if (source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage bitmap && bitmap.PixelWidth > 0)
        {
            double scale = XamlRoot?.RasterizationScale ?? 1;
            image.MaxWidth = bitmap.PixelWidth / scale;
        }
        PageStack.Children.Add(image);
        ImageScroller.Visibility = Visibility.Visible;
    }

    private void Reset()
    {
        Stop();
        Player.Visibility = Visibility.Collapsed;
        PageStack.Children.Clear();
        _pdfPages.Clear();
        _rendering.Clear();
        _renderedWidth = 0;
        ImageScroller.Visibility = Visibility.Collapsed;
        ImageScroller.ChangeView(0, 0, null, disableAnimation: true);
        PreviewText.Blocks.Clear();
        VsCodeButton.Visibility = Visibility.Collapsed;
        TextScroller.Visibility = Visibility.Collapsed;
        FolderScroller.Visibility = Visibility.Collapsed;
        FolderGrid.Children.Clear();
        TableScroller.Visibility = Visibility.Collapsed;
        TableGrid.Children.Clear();
        TableGrid.ColumnDefinitions.Clear();
        MetaScroller.Visibility = Visibility.Collapsed;
        MetaStack.Children.Clear();
        HandlerSurface.Visibility = Visibility.Collapsed;
        PageBar.Visibility = Visibility.Collapsed;
        Placeholder.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = false;
    }

    // ---------------------------------------------------------------- Tablo (CSV, TSV, Parquet)

    private const double RowHeight = 22;

    private async Task ShowTableAsync(FileItem item, int version)
    {
        TableData data;
        try
        {
            data = await TabularPreview.ReadAsync(item.FullPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (version != _version) return;
            CrashLog.Write("Tablo önizleme", ex);
            // CSV ise düz metne dön; Parquet bozuksa bilgi ver.
            var text = item.Kind == FileKind.Text ? await FilePreview.ReadTextHeadAsync(item.FullPath) : null;
            if (version != _version) return;
            if (text is not null)
            {
                ShowCode(text, null);
                TextScroller.Visibility = Visibility.Visible;
            }
            else ShowPlaceholder("\uE80A", "Tablo okunamadı: " + ex.Message);
            return;
        }
        if (version != _version) return;
        if (data.Columns.Count == 0)
        {
            ShowPlaceholder("\uE80A", data.Details.Length > 0 ? data.Details : "Boş tablo");
            return;
        }

        BuildTable(data);
        TableScroller.Visibility = Visibility.Visible;
        TableScroller.ChangeView(0, 0, null, disableAnimation: true);

        string rows = data.TotalRows is { } total
            ? total > data.Rows.Count ? $"{total:N0} satırın ilk {data.Rows.Count:N0}'ü" : $"{total:N0} satır"
            : $"ilk {data.Rows.Count:N0} satır";
        KindText.Text = Path.GetExtension(item.FullPath).Equals(".parquet", StringComparison.OrdinalIgnoreCase) ? "Parquet tablosu" : "Ayrılmış metin tablosu";
        ExtraText.Text = $"{data.Columns.Count} sütun · {rows}\n{data.Details}";
    }

    /// <summary>
    /// Her sütun tek bir çok satırlı TextBlock: binlerce hücre öğesi yerine birkaç düzine öğe;
    /// satırlar sabit satır yüksekliğiyle hizalanır, arkadaki şeritler okunurluğu sağlar.
    /// </summary>
    private void BuildTable(TableData data)
    {
        int columns = data.Columns.Count;
        int rows = data.Rows.Count;
        var stripeStyle = (Style)Resources["TableStripeStyle"];
        var headerStyle = (Style)Resources["TableHeaderStyle"];
        var mono = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas");

        // Satır numarası sütunu
        int digits = Math.Max(2, rows.ToString().Length);
        TableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(digits * 7.5 + 16) });
        for (int c = 0; c < columns; c++)
        {
            int longest = Math.Max(data.Columns[c].Length + 2, data.ColumnTypes.Count > c ? data.ColumnTypes[c].Length : 0);
            foreach (var row in data.Rows) longest = Math.Max(longest, row[c].Length);
            double width = Math.Clamp(longest * 7.2 + 20, 48, 300);
            TableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        }

        var header = new Border { Style = headerStyle };
        Grid.SetColumnSpan(header, columns + 1);
        TableGrid.Children.Add(header);

        var stripes = new StackPanel();
        for (int r = 0; r < rows; r++)
            stripes.Children.Add(r % 2 == 1 ? new Border { Height = RowHeight, Style = stripeStyle } : new Border { Height = RowHeight });
        Grid.SetRow(stripes, 1);
        Grid.SetColumnSpan(stripes, columns + 1);
        TableGrid.Children.Add(stripes);

        TableGrid.Children.Add(MakeColumn(string.Join("\n", Enumerable.Range(1, rows)), 0, numeric: true, mono, secondary: true));
        for (int c = 0; c < columns; c++)
        {
            var title = new StackPanel { Padding = new Thickness(8, 6, 8, 6), Spacing = 0 };
            title.Children.Add(new TextBlock
            {
                Text = data.Columns[c],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (data.ColumnTypes.Count > c)
                title.Children.Add(new TextBlock { Text = data.ColumnTypes[c], Style = (Style)Resources["MetaDetailStyle"], FontSize = 11 });
            ToolTipService.SetToolTip(title, data.Columns[c]);
            Grid.SetColumn(title, c + 1);
            TableGrid.Children.Add(title);

            bool numeric = data.ColumnTypes.Count > c && data.ColumnTypes[c] is "sayı" or "ondalık" or "tam sayı";
            int column = c;
            TableGrid.Children.Add(MakeColumn(string.Join("\n", data.Rows.Select(r => r[column])), c + 1, numeric, mono, secondary: false));
        }
    }

    private static TextBlock MakeColumn(string text, int column, bool numeric, Microsoft.UI.Xaml.Media.FontFamily mono, bool secondary)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 12,
            LineHeight = RowHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = numeric ? TextAlignment.Right : TextAlignment.Left,
            Padding = new Thickness(8, 0, 8, 0),
            IsTextSelectionEnabled = !secondary,
        };
        if (numeric) block.FontFamily = mono;
        if (secondary)
        {
            block.Opacity = 0.5;
            block.FontSize = 11;
        }
        Grid.SetRow(block, 1);
        Grid.SetColumn(block, column);
        return block;
    }

    // ---------------------------------------------------------------- Yapı özeti (Power BI)

    private const int MetaEntriesShown = 150;

    private async Task ShowMetaAsync(FileItem item, int version)
    {
        MetaReport report;
        try
        {
            report = await PowerBiInspector.InspectAsync(item.FullPath, item.IsDirectory);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (version != _version) return;
            CrashLog.Write("Power BI önizleme", ex);
            if (item.IsDirectory) await ShowFolderAsync(item, version);
            else ShowPlaceholder("\uE9D9", "Power BI dosyası okunamadı: " + ex.Message);
            return;
        }
        if (version != _version) return;

        KindText.Text = item.IsDirectory
            ? item.Name.EndsWith(".Report", StringComparison.OrdinalIgnoreCase) ? "Power BI raporu (PBIP klasörü)" : "Power BI anlam modeli (PBIP klasörü)"
            : Path.GetExtension(item.FullPath).ToLowerInvariant() switch
            {
                ".pbix" => "Power BI raporu",
                ".pbit" => "Power BI şablonu",
                ".pbip" => "Power BI projesi",
                _ => "Tablosal model (TMSL)",
            };
        ExtraText.Text = string.Join(" · ", report.Badges);

        if (report.Badges.Count > 0)
        {
            var wrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var badge in report.Badges)
            {
                wrap.Children.Add(new Border
                {
                    Style = (Style)Resources["MetaBadgeStyle"],
                    Child = new TextBlock { Text = badge, Style = (Style)Resources["MetaBadgeTextStyle"] },
                });
            }
            MetaStack.Children.Add(new ScrollViewer
            {
                Content = wrap,
                HorizontalScrollMode = ScrollMode.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollMode = ScrollMode.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
        }

        foreach (var note in report.Notes)
            MetaStack.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational, Message = note });

        var mono = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas");
        foreach (var section in report.Sections)
        {
            var panel = new StackPanel { Spacing = 4 };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 2) };
            head.Children.Add(new FontIcon { Glyph = section.Glyph, FontSize = 14 });
            head.Children.Add(new TextBlock { Text = section.Title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
            head.Children.Add(new TextBlock { Text = section.Entries.Count.ToString(), Style = (Style)Resources["MetaDetailStyle"], VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(head);

            foreach (var entry in section.Entries.Take(MetaEntriesShown))
                panel.Children.Add(MakeMetaEntry(entry, mono));
            if (section.Entries.Count > MetaEntriesShown)
                panel.Children.Add(new TextBlock { Text = $"… ve {section.Entries.Count - MetaEntriesShown} tane daha", Style = (Style)Resources["MetaDetailStyle"] });
            MetaStack.Children.Add(panel);
        }

        if (MetaStack.Children.Count == 0)
        {
            ShowPlaceholder("\uE9D9", "Okunabilir bir yapı bulunamadı");
            return;
        }
        MetaScroller.Visibility = Visibility.Visible;
        MetaScroller.ChangeView(0, 0, null, disableAnimation: true);
    }

    private UIElement MakeMetaEntry(MetaEntry entry, Microsoft.UI.Xaml.Media.FontFamily mono)
    {
        var header = new StackPanel { Spacing = 0 };
        header.Children.Add(new TextBlock { Text = entry.Title, TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        if (!string.IsNullOrEmpty(entry.Detail))
            header.Children.Add(new TextBlock { Text = entry.Detail, Style = (Style)Resources["MetaDetailStyle"] });

        if (string.IsNullOrWhiteSpace(entry.Code))
        {
            header.Padding = new Thickness(26, 2, 0, 2);
            return header;
        }

        // Kod (DAX / M) yalnızca açılınca oluşturulur.
        var expander = new Expander
        {
            Header = header,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 8, 12, 8),
        };
        expander.Expanding += (_, _) =>
        {
            if (expander.Content is not null) return;
            expander.Content = new TextBlock
            {
                Text = entry.Code,
                FontFamily = mono,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            };
        };
        return expander;
    }

    // ---------------------------------------------------------------- Kod (sözdizimi renkleri)

    private void ShowCode(string text, string? language)
    {
        PreviewText.Blocks.Clear();
        PreviewText.Blocks.Add(BuildCodeParagraph(text, language));
        TextScroller.Visibility = Visibility.Visible;
        TextScroller.ChangeView(0, 0, null, disableAnimation: true);
        if (language is not null) KindText.Text = $"{language} kodu · {_current?.Extension}";
    }

    private Paragraph BuildCodeParagraph(string text, string? language)
    {
        var paragraph = new Paragraph();
        var palette = Palette(ActualTheme);
        // Aynı türden ardışık belirteçleri birleştir: daha az Run, daha hızlı çizim.
        TokenKind? pendingKind = null;
        var pending = new System.Text.StringBuilder();
        void Flush()
        {
            if (pending.Length == 0 || pendingKind is null) return;
            var run = new Run { Text = pending.ToString() };
            if (palette.TryGetValue(pendingKind.Value, out var brush)) run.Foreground = brush;
            if (pendingKind is TokenKind.Heading) run.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            if (pendingKind is TokenKind.Comment) run.FontStyle = Windows.UI.Text.FontStyle.Italic;
            paragraph.Inlines.Add(run);
            pending.Clear();
        }
        foreach (var token in SyntaxHighlighter.Tokenize(text, language))
        {
            if (token.Kind != pendingKind) Flush();
            pendingKind = token.Kind;
            pending.Append(token.Text);
        }
        Flush();
        return paragraph;
    }

    private static Dictionary<TokenKind, Brush>? _darkPalette, _lightPalette;

    /// <summary>VS Code Dark+/Light+ renklerine yakın palet.</summary>
    private static Dictionary<TokenKind, Brush> Palette(ElementTheme theme)
    {
        static SolidColorBrush B(uint rgb) => new(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        if (theme == ElementTheme.Light)
        {
            return _lightPalette ??= new()
            {
                [TokenKind.Comment] = B(0x008000), [TokenKind.String] = B(0xA31515), [TokenKind.Keyword] = B(0x0000FF),
                [TokenKind.Control] = B(0xAF00DB), [TokenKind.Number] = B(0x098658), [TokenKind.Type] = B(0x267F99),
                [TokenKind.Function] = B(0x795E26), [TokenKind.Tag] = B(0x800000), [TokenKind.Attribute] = B(0xE50000),
                [TokenKind.Property] = B(0x0451A5), [TokenKind.Heading] = B(0x0000FF),
            };
        }
        return _darkPalette ??= new()
        {
            [TokenKind.Comment] = B(0x6A9955), [TokenKind.String] = B(0xCE9178), [TokenKind.Keyword] = B(0x569CD6),
            [TokenKind.Control] = B(0xC586C0), [TokenKind.Number] = B(0xB5CEA8), [TokenKind.Type] = B(0x4EC9B0),
            [TokenKind.Function] = B(0xDCDCAA), [TokenKind.Tag] = B(0x569CD6), [TokenKind.Attribute] = B(0x9CDCFE),
            [TokenKind.Property] = B(0x9CDCFE), [TokenKind.Heading] = B(0x569CD6),
        };
    }

    // ---------------------------------------------------------------- Jupyter not defteri

    private async Task ShowNotebookAsync(FileItem item, int version)
    {
        Notebook notebook;
        try
        {
            notebook = await NotebookReader.ReadAsync(item.FullPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (version != _version) return;
            CrashLog.Write("Not defteri önizleme", ex);
            var raw = await FilePreview.ReadTextHeadAsync(item.FullPath);
            if (version != _version) return;
            if (raw is null) ShowPlaceholder(item.Glyph, "Not defteri okunamadı");
            else ShowCode(raw, "JSON");
            return;
        }
        if (version != _version) return;

        var language = SyntaxHighlighter.LanguageFor("x." + notebook.Language switch
        {
            "python" or "python3" or "pyspark" => "py",
            "r" => "r",
            "sql" or "sparksql" => "sql",
            "scala" => "scala",
            "csharp" or "c#" or ".net-csharp" => "cs",
            "javascript" or "typescript" => "js",
            "powershell" => "ps1",
            _ => "py",
        });
        int codeCells = notebook.Cells.Count(c => c.IsCode);
        KindText.Text = $"Jupyter not defteri · {notebook.KernelName ?? notebook.Language}";
        ExtraText.Text = $"{notebook.TotalCells} hücre · {codeCells} kod" + (notebook.TotalCells > notebook.Cells.Count ? $" · ilk {notebook.Cells.Count} gösteriliyor" : string.Empty);

        var mono = new FontFamily("Cascadia Mono, Consolas");
        var codeBackground = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        var border = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var critical = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];

        foreach (var cell in notebook.Cells)
        {
            if (cell.Source.Length == 0 && cell.Outputs.Count == 0) continue;
            if (cell.IsMarkdown)
            {
                var markdown = new RichTextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 13 };
                markdown.Blocks.Add(BuildCodeParagraph(cell.Source, "Markdown"));
                MetaStack.Children.Add(markdown);
                continue;
            }
            if (!cell.IsCode)
            {
                MetaStack.Children.Add(new TextBlock { Text = cell.Source, FontFamily = mono, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = secondary });
                continue;
            }

            var block = new StackPanel { Spacing = 4 };
            block.Children.Add(new TextBlock
            {
                Text = cell.ExecutionCount is { } n ? $"[{n}]" : "[ ]",
                FontFamily = mono,
                FontSize = 11,
                Foreground = secondary,
            });
            var code = new RichTextBlock { FontFamily = mono, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            code.Blocks.Add(BuildCodeParagraph(cell.Source, language));
            block.Children.Add(new Border
            {
                Background = codeBackground,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Child = code,
            });
            foreach (var output in cell.Outputs)
            {
                if (output.PngBase64 is { } png)
                {
                    var image = new Image { Stretch = Stretch.Uniform, MaxHeight = 360, HorizontalAlignment = HorizontalAlignment.Left };
                    try
                    {
                        var bytes = Convert.FromBase64String(png);
                        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                        using var memory = new MemoryStream(bytes);
                        await bitmap.SetSourceAsync(memory.AsRandomAccessStream());
                        if (version != _version) return;
                        image.Source = bitmap;
                        block.Children.Add(image);
                    }
                    catch (Exception ex) when (ex is FormatException or IOException or System.Runtime.InteropServices.COMException)
                    {
                        // bozuk görsel: atla
                    }
                }
                else if (!string.IsNullOrWhiteSpace(output.Text))
                {
                    block.Children.Add(new TextBlock
                    {
                        Text = output.Text,
                        FontFamily = mono,
                        FontSize = 11.5,
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true,
                        Foreground = output.IsError ? critical : secondary,
                        Margin = new Thickness(10, 0, 0, 0),
                    });
                }
            }
            MetaStack.Children.Add(block);
        }

        if (MetaStack.Children.Count == 0)
        {
            ShowPlaceholder(item.Glyph, "Not defteri boş");
            return;
        }
        MetaScroller.Visibility = Visibility.Visible;
        MetaScroller.ChangeView(0, 0, null, disableAnimation: true);
    }

    // ---------------------------------------------------------------- VS Code

    private void UpdateVsCodeButton(FileItem item)
    {
        if (!VsCode.Suits(item) || item.IsRemote)
        {
            VsCodeButton.Visibility = Visibility.Collapsed;
            return;
        }
        bool installed = VsCode.IsInstalled;
        VsCodeButton.Visibility = Visibility.Visible;
        VsCodeButton.IsEnabled = installed;
        ToolTipService.SetToolTip(VsCodeButton, installed
            ? (item.IsDirectory ? "Klasörü VS Code'da aç" : "Dosyayı VS Code'da aç")
            : "Visual Studio Code bu bilgisayarda bulunamadı");
    }

    private void VsCodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is { } item) VsCode.Open(item.FullPath);
    }

    private string? _hydrateAllowed;

    private async void PlaceholderAction_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { } item) return;
        _hydrateAllowed = item.FullPath;
        _current = null; // aynı öğeyi yeniden göstermeye izin ver
        await ShowAsync(item);
    }

    private void ShowPlaceholder(string glyph, string text)
    {
        PlaceholderAction.Visibility = Visibility.Collapsed;
        PlaceholderIcon.Glyph = glyph;
        PlaceholderText.Text = text;
        Placeholder.Visibility = Visibility.Visible;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
