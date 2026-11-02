using System.Threading;
using System.Windows;
using System.IO;
namespace Capsule.App;
public partial class App : System.Windows.Application
{
    private Mutex? mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        mutex = new Mutex(true, "Local\\LingdongCapsule", out var first);
        if (!first) { Shutdown(); return; }
        base.OnStartup(e);
        DispatcherUnhandledException += (_, a) => { MessageBox.Show(a.Exception.Message, "灵动胶囊发生错误"); Shutdown(1); };
        MainWindow = new MainWindow(); MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { mutex?.Dispose(); base.OnExit(e); }
}
