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
    private uint _pdfPage;
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
                    var (folders, files, capped) = await FilePreview.CountAsync(item.FullPath);
                    if (version != _version) return;
                    ShowPlaceholder("", capped ? "10.000+ öğe" : $"{folders} klasör, {files} dosya");
                    break;

                case FileKind.Image:
                    var image = await FilePreview.GetFullImageAsync(item.FullPath, 1200);
                    if (version != _version) return;
                    if (image is null) { ShowPlaceholder(item.Glyph, "Önizleme yok"); break; }
                    ShowImage(image);
                    if (image.PixelWidth > 0) ExtraText.Text = $"{image.PixelWidth} × {image.PixelHeight} · Ctrl+tekerlek: yakınlaştır";
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
                    _pdfPage = 0;
                    await RenderPdfPageAsync(version);
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

    // ---------------------------------------------------------------- PDF

    private async Task RenderPdfPageAsync(int version)
    {
        if (_pdf is null) return;
        double width = Math.Max(600, Stage.ActualWidth * (XamlRoot?.RasterizationScale ?? 1) * 1.5);
        var page = await FilePreview.RenderPageAsync(_pdf, _pdfPage, (uint)Math.Min(2400, width));
        if (version != _version || page is null) return;
        ShowImage(page);
        PageBar.Visibility = _pdf.PageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        PageText.Text = $"Sayfa {_pdfPage + 1} / {_pdf.PageCount}";
        ExtraText.Text = $"{_pdf.PageCount} sayfa · tekerlek: sayfa değiştir";
    }

    private async void PrevPage_Click(object sender, RoutedEventArgs e) => await GoToPageAsync(-1);

    private async void NextPage_Click(object sender, RoutedEventArgs e) => await GoToPageAsync(+1);

    private async Task GoToPageAsync(int delta)
    {
        if (_pdf is null) return;
        long target = Math.Clamp((long)_pdfPage + delta, 0, _pdf.PageCount - 1);
        if (target == _pdfPage) return;
        _pdfPage = (uint)target;
        await RenderPdfPageAsync(_version);
        ImageScroller.ChangeView(0, 0, 1, disableAnimation: true);
    }

    private async void ImageScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_pdf is null) return;
        if (e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control)) return; // yakınlaştırma
        if (ImageScroller.ZoomFactor > 1.01f) return; // yakınken normal kaydırma

        int delta = e.GetCurrentPoint(ImageScroller).Properties.MouseWheelDelta;
        e.Handled = true;
        await GoToPageAsync(delta < 0 ? +1 : -1);
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
        PreviewImage.Source = source;
        ImageScroller.Visibility = Visibility.Visible;
    }

    private void Reset()
    {
        Stop();
        Player.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        ImageScroller.Visibility = Visibility.Collapsed;
        ImageScroller.ChangeView(0, 0, 1, disableAnimation: true);
        PreviewText.Text = string.Empty;
        TextScroller.Visibility = Visibility.Collapsed;
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
