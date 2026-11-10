using System.Text;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using ZeroFileCommander.Models;

namespace ZeroFileCommander.Services;

/// <summary>Önizleme paneli ve hover kartı için görsel/metin üretir.</summary>
public static class FilePreview
{
    /// <summary>Görsel/video için Windows küçük resmi, PDF için ilk sayfanın çizimi.</summary>
    public static async Task<BitmapImage?> GetVisualAsync(string path, FileKind kind, uint size)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            if (kind == FileKind.Pdf) return await RenderPdfPageAsync(file, 0, size);

            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, size, ThumbnailOptions.ResizeThumbnail);
            if (thumb is null || thumb.Size == 0) return null;
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumb);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<BitmapImage?> GetFullImageAsync(string path, int decodeWidth)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage { DecodePixelWidth = decodeWidth };
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch
        {
            // HEIC gibi codec'i olmayan formatlarda Windows küçük resmine düş.
            return await GetVisualAsync(path, FileKind.Image, (uint)decodeWidth);
        }
    }

    public static async Task<(BitmapImage? Page, uint PageCount)> RenderPdfAsync(string path, uint width)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var doc = await PdfDocument.LoadFromFileAsync(file);
            return (await RenderPageAsync(doc, 0, width), doc.PageCount);
        }
        catch
        {
            return (null, 0);
        }
    }

    public static async Task<PdfDocument?> LoadPdfAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            return await PdfDocument.LoadFromFileAsync(file);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<BitmapImage?> RenderPdfPageAsync(StorageFile file, uint index, uint width)
    {
        var doc = await PdfDocument.LoadFromFileAsync(file);
        return await RenderPageAsync(doc, index, width);
    }

    public static async Task<BitmapImage?> RenderPageAsync(PdfDocument doc, uint index, uint width)
    {
        if (doc.PageCount == 0) return null;
        using var page = doc.GetPage(Math.Min(index, doc.PageCount - 1));
        using var stream = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = width });
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    /// <summary>Dosyanın başını okur (en fazla <paramref name="maxChars"/> karakter).</summary>
    public static Task<string?> ReadTextHeadAsync(string path, int maxChars = 24_000) => Task.Run<string?>(() =>
    {
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[maxChars];
            int read = reader.ReadBlock(buffer, 0, buffer.Length);
            var text = new string(buffer, 0, read);
            if (text.Contains('\0')) return null; // ikili dosya
            return reader.EndOfStream ? text : text + "\n…";
        }
        catch
        {
            return null;
        }
    });

    /// <summary>Klasördeki öğe sayısı (ilk 10.000'e kadar).</summary>
    public static Task<(int Folders, int Files, bool Capped)> CountAsync(string folder) => Task.Run(() =>
    {
        int folders = 0, files = 0;
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true };
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options))
            {
                if (entry is DirectoryInfo) folders++; else files++;
                if (folders + files >= 10_000) return (folders, files, true);
            }
        }
        catch
        {
            // erişim yoksa sıfır döner
        }
        return (folders, files, false);
    });

    /// <summary>Klasör boyutunu alt klasörlerle birlikte hesaplar.</summary>
    public static Task<long> FolderSizeAsync(string folder, CancellationToken token) => Task.Run(() =>
    {
        long total = 0;
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true };
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
        {
            token.ThrowIfCancellationRequested();
            total += file.Length;
        }
        return total;
    }, token);
}
