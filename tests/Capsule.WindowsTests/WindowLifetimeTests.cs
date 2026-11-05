using Capsule.App;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

internal static class WindowLifetimeTests
{
    internal static async Task RunAsync()
    {
        var root=Path.Combine(Environment.CurrentDirectory,"artifacts","window-lifetime-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var settings=new Settings();using var shortcuts=new GlobalShortcuts(settings);var writes=0;
        SettingsWindow Create()=>new(settings,()=>Task.FromResult("Owned fixture"),()=>writes++,()=>{},_=>{},()=>{},()=>new Dictionary<string,string>(),shortcuts,_=>{},()=>[],_=>{},_=>{},()=>[],_=>{},(_,_)=>false,()=>[],(_,_)=>{},()=>false){ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-10000,Top=-10000};
        var references=new List<WeakReference>();var samples=new List<object>();
        for(var cycle=0;cycle<16;cycle++)
        {
            references.AddRange(await CycleAsync(Create,cycle%2==0));
            await Dispatcher.CurrentDispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(60);
            // Collect only this disposable test process, never the real candidate.
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            using var process=Process.GetCurrentProcess();process.Refresh();
            samples.Add(new{cycle,alive=references.Count(item=>item.IsAlive),handles=process.HandleCount,privateMiB=process.PrivateMemorySize64/1048576d,managedMiB=GC.GetTotalMemory(false)/1048576d});
        }
        await Task.Delay(200);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        var alive=references.Count(item=>item.IsAlive);
        await File.WriteAllTextAsync(Path.Combine(root,"lifetime.json"),JsonSerializer.Serialize(new{alive,writes,samples,scope="Owned offscreen windows with forced GC in this test process only; no media publication, clipboard, payment, daily preference write or production GC. Not a long-run resource benchmark."}));
        if(writes!=0 || alive>1)throw new Exception("Closed windows retained across cycles or wrote preferences; alive="+alive+"; evidence: "+root);
        Console.WriteLine("PASS repeated settings/support close releases window objects; retained="+alive+"; no daily profile writes or production collection. Evidence: "+root);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference[]> CycleAsync(Func<SettingsWindow> create,bool support)
    {
        var page=create();page.Show();page.SelectPage("关于软件");await Task.Delay(60);var pageReference=new WeakReference(page);
        if(!support){page.Close();return [pageReference];}
        var dialog=new SupportWindow{Owner=page,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-10000,Top=-10000};dialog.Show();await Task.Delay(60);var dialogReference=new WeakReference(dialog);dialog.Close();page.Close();return [pageReference,dialogReference];
    }
}
