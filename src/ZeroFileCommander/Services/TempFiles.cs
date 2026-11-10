namespace ZeroFileCommander.Services;

/// <summary>
/// Uygulamanın geçici dosyaları (arşivden/sunucudan önizleme kopyaları, pano için çıkarılanlar).
/// Kapanışta silinir; dışarıda açık kalıp silinemeyenler sonraki açılışta temizlenir.
/// </summary>
public static class TempFiles
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ZeroFileCommander");

    /// <summary>Kapanışta: önizleme kopyaları. Pano kopyaları başka uygulamaya yapıştırılabileceği için kalır.</summary>
    public static void CleanOnExit()
    {
        Delete(Path.Combine(Root, "archive"), TimeSpan.Zero);
        Delete(Path.Combine(Root, "remote"), TimeSpan.Zero);
    }

    /// <summary>Açılışta: önceki oturumlardan kalanlar (pano kopyaları bir günden eskiyse).</summary>
    public static void CleanStale() => Task.Run(() =>
    {
        Delete(Path.Combine(Root, "archive"), TimeSpan.Zero);
        Delete(Path.Combine(Root, "remote"), TimeSpan.Zero);
        Delete(Path.Combine(Root, "clipboard"), TimeSpan.FromDays(1));
    });

    private static void Delete(string folder, TimeSpan olderThan)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            var cutoff = DateTime.Now - olderThan;
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (File.GetLastWriteTime(file) > cutoff && olderThan > TimeSpan.Zero) continue;
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // dışarıda açık: sonraki açılışta
                }
            }
            foreach (var dir in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
