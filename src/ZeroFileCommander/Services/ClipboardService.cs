using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace ZeroFileCommander.Services;

public sealed record ClipboardFiles(IReadOnlyList<string> Paths, bool IsCut);

/// <summary>
/// Windows panosu üzerinden kes/kopyala/yapıştır. Dosyalar StorageItem olarak
/// konduğu için Dosya Gezgini ile iki yönlü çalışır.
/// </summary>
public static class ClipboardService
{
    public static async Task SetFilesAsync(IEnumerable<string> paths, bool cut)
    {
        var items = await LoadStorageItemsAsync(paths);
        if (items.Count == 0) return;

        var package = new DataPackage
        {
            RequestedOperation = cut ? DataPackageOperation.Move : DataPackageOperation.Copy,
        };
        package.SetStorageItems(items, readOnly: false);
        Clipboard.SetContent(package);
        TryFlush();
    }

    public static void SetText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        TryFlush();
    }

    public static bool HasFiles()
    {
        try
        {
            return Clipboard.GetContent().Contains(StandardDataFormats.StorageItems);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<ClipboardFiles?> GetFilesAsync()
    {
        try
        {
            var view = Clipboard.GetContent();
            if (!view.Contains(StandardDataFormats.StorageItems)) return null;
            var items = await view.GetStorageItemsAsync();
            var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            bool cut = view.RequestedOperation.HasFlag(DataPackageOperation.Move)
                       && !view.RequestedOperation.HasFlag(DataPackageOperation.Copy);
            return paths.Count == 0 ? null : new ClipboardFiles(paths, cut);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<string?> GetTextAsync()
    {
        try
        {
            var view = Clipboard.GetContent();
            return view.Contains(StandardDataFormats.Text) ? await view.GetTextAsync() : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Clear()
    {
        try { Clipboard.Clear(); } catch { /* pano meşgul olabilir */ }
    }

    public static async Task<List<IStorageItem>> LoadStorageItemsAsync(IEnumerable<string> paths)
    {
        var result = new List<IStorageItem>();
        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                    result.Add(await StorageFolder.GetFolderFromPathAsync(path));
                else if (File.Exists(path))
                    result.Add(await StorageFile.GetFileFromPathAsync(path));
            }
            catch
            {
                // Erişilemeyen öğeyi atla.
            }
        }
        return result;
    }

    private static void TryFlush()
    {
        try { Clipboard.Flush(); } catch { /* uygulama kapanınca kaybolabilir, sorun değil */ }
    }
}
