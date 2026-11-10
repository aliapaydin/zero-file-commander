using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Core;
using Windows.Storage;
using ZeroFileCommander.Models;
using ZeroFileCommander.Services;

namespace ZeroFileCommander.Controls;

/// <summary>Sağdaki önizleme paneli: görsel, PDF, metin/kod, ses ve video.</summary>
public sealed partial class PreviewPane : UserControl
{
    private int _version;
    private FileItem? _current;

    public event EventHandler? CloseRequested;

    public PreviewPane()
    {
        InitializeComponent();
    }

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
            NameText.Text = string.Empty;
            KindText.Text = string.Empty;
            SizeText.Text = DateText.Text = ExtraText.Text = string.Empty;
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
                    var image = await FilePreview.GetFullImageAsync(item.FullPath, 900);
                    if (version != _version) return;
                    if (image is null) { ShowPlaceholder(item.Glyph, "Önizleme yok"); break; }
                    PreviewImage.Source = image;
                    PreviewImage.Visibility = Visibility.Visible;
                    if (image.PixelWidth > 0) ExtraText.Text = $"{image.PixelWidth} × {image.PixelHeight}";
                    break;

                case FileKind.Pdf:
                    var (page, count) = await FilePreview.RenderPdfAsync(item.FullPath, 900);
                    if (version != _version) return;
                    if (page is null) { ShowPlaceholder(item.Glyph, "PDF açılamadı"); break; }
                    PreviewImage.Source = page;
                    PreviewImage.Visibility = Visibility.Visible;
                    ExtraText.Text = $"{count} sayfa";
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
                    var visual = await FilePreview.GetVisualAsync(item.FullPath, item.Kind, 256);
                    if (version != _version) return;
                    if (visual is not null)
                    {
                        PreviewImage.Source = visual;
                        PreviewImage.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        ShowPlaceholder(item.Glyph, "Bu tür için önizleme yok");
                    }
                    break;
            }
        }
        catch
        {
            if (version == _version) ShowPlaceholder(item.Glyph, "Önizleme yüklenemedi");
        }
        finally
        {
            if (version == _version) LoadingRing.IsActive = false;
        }
    }

    public void Play() => Player.MediaPlayer?.Play();

    /// <summary>Çalan medyayı durdurur ve kaynakları bırakır.</summary>
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
    }

    private void Reset()
    {
        Stop();
        Player.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewText.Text = string.Empty;
        TextScroller.Visibility = Visibility.Collapsed;
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
