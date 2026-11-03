using System.Threading;
using System.Windows;
using System.IO;
namespace Capsule.App;

public partial class App : System.Windows.Application
{
    private Mutex? mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--install"))
        {
            base.OnStartup(e);
            Design.ApplySystemTheme();
            MainWindow = new InstallWindow(); MainWindow.Show(); return;
        }
        mutex = new Mutex(true, "Local\\LingdongCapsule", out var first);
        if (!first) { Shutdown(); return; }
        base.OnStartup(e);
        Design.ApplySystemTheme();
        DispatcherUnhandledException += (_, a) => { a.Handled = true; Design.PresentMessage("灵动胶囊发生错误", a.Exception.Message); Shutdown(1); };
        MainWindow = new MainWindow(); MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { mutex?.Dispose(); base.OnExit(e); }
}
