using System.Threading;
using System.Windows;
using System.IO;
namespace Capsule.App;

public partial class App : System.Windows.Application
{
    private SingleInstance? instance;
    internal AppDiagnostics? Diagnostics{get;private set;}
    internal bool SuppressStartupForTests{get;set;}
    protected override void OnStartup(StartupEventArgs e)
    {
        if(SuppressStartupForTests){base.OnStartup(e);return;}
        if (e.Args.Contains("--install"))
        {
            base.OnStartup(e);
            Design.ApplySystemTheme();
            MainWindow = new InstallWindow(); MainWindow.Show(); return;
        }
        const string instanceName = "Local\\LingdongCapsule";
        instance = new SingleInstance(instanceName, () =>
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is MainWindow window) window.OpenSettings();
            });
        });
        if (!instance.IsPrimary)
        {
            if (!e.Args.Contains("--startup")) SingleInstance.Notify(instanceName);
            Shutdown(); return;
        }
        base.OnStartup(e);
        Diagnostics=AppDiagnostics.FromArguments(e.Args);
        Design.ApplySystemTheme();
        DispatcherUnhandledException += (_, a) => { Diagnostics?.Unhandled(a.Exception);a.Handled = true; Design.PresentMessage("灵动胶囊发生错误", a.Exception.Message); Shutdown(1); };
        MainWindow = new MainWindow(); MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { Diagnostics?.Finish(e.ApplicationExitCode);instance?.Dispose(); base.OnExit(e); }
}
