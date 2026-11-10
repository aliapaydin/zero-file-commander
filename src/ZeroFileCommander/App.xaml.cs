using Microsoft.UI.Xaml;
using ZeroFileCommander.Services;

namespace ZeroFileCommander;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();

        // Beklenmeyen hatalar uygulamayı kapatmasın; günlüğe yazılsın.
        UnhandledException += (_, e) =>
        {
            CrashLog.Write("UI", e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("Task", e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write("AppDomain", e.ExceptionObject as Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // ANSI/OEM kod sayfaları (Türkçe CSV'ler, eski ZIP adları) için.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        Services.TempFiles.CleanStale();
        _window = new MainWindow();
        _window.Activate();
    }
}
