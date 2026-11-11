using System.Text;
using Windows.Storage;

namespace ZeroFileCommander.Services;

/// <summary>
/// Yakalanmayan hataları %LocalAppData%\Packages\…\LocalState\logs\errors.log dosyasına yazar.
/// Yardım menüsündeki "Hata günlüğünü aç" bu dosyayı açar.
/// </summary>
public static class CrashLog
{
    public static string FolderPath
    {
        get
        {
            try
            {
                return Path.Combine(ApplicationData.Current.LocalFolder.Path, "logs");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "ZeroFileCommander", "logs");
            }
        }
    }

    public static string FilePath => Path.Combine(FolderPath, "errors.log");

    public static void Write(string source, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            var text = new StringBuilder()
                .AppendLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {source} ===")
                .AppendLine(exception?.ToString() ?? Loc.T("(ayrıntı yok)"))
                .AppendLine();
            File.AppendAllText(FilePath, text.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Günlük yazılamıyorsa yapacak bir şey yok.
        }
    }
}
