using System.Threading;
using System.Windows;
using System.IO;
namespace Capsule.App;

public partial class App : System.Windows.Application
{
    private SingleInstance? instance;
    internal AppDiagnostics? Diagnostics{get;private set;}
    internal bool SuppressStartupForTests{get;set;}
    private string? maintenanceStage;
    protected override async void OnStartup(StartupEventArgs e)
    {
        if(SuppressStartupForTests){base.OnStartup(e);return;}
        if(e.Args.Contains("--uninstall") || e.Args.Contains("--uninstall-staged"))
        {
            base.OnStartup(e);Design.ApplySystemTheme();
            if(e.Args.Contains("--uninstall-staged"))
            {
                try{var root=AppContext.BaseDirectory;var target=MaintenanceBootstrap.ReadTarget(root);maintenanceStage=root;MainWindow=new MaintenanceWindow(true,target);MainWindow.Show();}
                catch(Exception error){Design.PresentMessage("无法打开卸载界面",error.Message);Shutdown(1);}
                return;
            }
            ShutdownMode=ShutdownMode.OnExplicitShutdown;var preparing=new MaintenanceWindow(true);MainWindow=preparing;preparing.BeginPreparation();preparing.Show();
            while(true)
            {
                try{var stage=await Task.Run(()=>MaintenanceBootstrap.PrepareUninstaller(AppContext.BaseDirectory));MaintenanceBootstrap.Launch(stage);preparing.EndPreparation();Shutdown();break;}
                catch(Exception error){preparing.EndPreparation();if(CardDialog.Show("卸载准备未完成",error.Message,"重试")==null){Shutdown(1);break;}preparing.BeginPreparation();}
            }
            return;
        }
        if (e.Args.Contains("--install"))
        {
            base.OnStartup(e);
            Design.ApplySystemTheme();
            MainWindow = new MaintenanceWindow(); MainWindow.Show(); return;
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
    protected override void OnExit(ExitEventArgs e) { Diagnostics?.Finish(e.ApplicationExitCode);instance?.Dispose();if(maintenanceStage!=null)try{MaintenanceBootstrap.ScheduleCleanup(maintenanceStage);}catch{/* Keep a failed-cleanup staging directory rather than expanding deletion scope. */}base.OnExit(e); }
}
