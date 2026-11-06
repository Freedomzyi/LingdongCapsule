using System.Reflection;
using System.Windows;
using Capsule.App;

namespace Capsule.Setup;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app=new Capsule.App.App{HostedMaintenance=true};app.InitializeComponent();
        SetupPayload? payload=null;
        SingleInstance? instance=null;
        app.Startup+=async(_,_)=>
        {
            const string name="Local\\LingdongCapsule.Setup";
            instance=new SingleInstance(name,()=>app.Dispatcher.BeginInvoke(()=>{if(app.MainWindow is {} window){if(window.WindowState==WindowState.Minimized)window.WindowState=WindowState.Normal;window.Activate();}}));
            if(!instance.IsPrimary){SingleInstance.Notify(name);app.Shutdown();return;}
            Design.ApplyMaintenancePreferences();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            var preparing=new MaintenanceWindow{Title="准备灵动胶囊安装"};app.MainWindow=preparing;preparing.BeginPreparation("正在准备安装文件…");preparing.Show();
            try
            {
                var assembly=typeof(Program).Assembly;var hash=assembly.GetCustomAttributes<AssemblyMetadataAttribute>().SingleOrDefault(item=>item.Key=="PayloadSHA256")?.Value ?? "";
                using var stream=assembly.GetManifestResourceStream("Capsule.Setup.Payload.zip") ?? throw new System.IO.IOException("安装文件不完整，请重新下载安装程序。");
                payload=await Task.Run(()=>SetupPayload.Extract(stream,hash));
                var window=new MaintenanceWindow(directory:payload.DirectoryPath);app.MainWindow=window;preparing.EndPreparation();preparing.Close();app.ShutdownMode=ShutdownMode.OnMainWindowClose;window.Show();
            }
            catch(Exception error){preparing.EndPreparation();Design.PresentMessage("无法准备安装文件",error.Message);app.Shutdown(1);}
        };
        app.Exit+=(_,_)=>{payload?.Dispose();instance?.Dispose();};app.Run();
    }
}
