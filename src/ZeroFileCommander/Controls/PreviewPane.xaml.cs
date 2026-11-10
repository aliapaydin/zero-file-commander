using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

        LoadingRing.IsActive = true;
        try
        {
            switch (item.Kind)
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
                    PreviewText.Text = text;
                    TextScroller.Visibility = Visibility.Visible;
                    TextScroller.ChangeView(0, 0, null, disableAnimation: true);
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
        PreviewText.Text = string.Empty;
        TextScroller.Visibility = Visibility.Collapsed;
        FolderScroller.Visibility = Visibility.Collapsed;
        FolderGrid.Children.Clear();
        HandlerSurface.Visibility = Visibility.Collapsed;
        PageBar.Visibility = Visibility.Collapsed;
        Placeholder.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = false;
    }

    private void ShowPlaceholder(string glyph, string text)
    {
        PlaceholderIcon.Glyph = glyph;
        PlaceholderText.Text = text;
        Placeholder.Visibility = Visibility.Visible;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
