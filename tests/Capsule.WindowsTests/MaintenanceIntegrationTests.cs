using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Capsule.App;

internal static class MaintenanceIntegrationTests
{
    private delegate bool Callback(IntPtr window,IntPtr unused);
    [DllImport("user32.dll")]private static extern bool EnumWindows(Callback callback,IntPtr unused);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
    [DllImport("user32.dll")]private static extern bool PostMessage(IntPtr window,uint message,IntPtr wp,IntPtr lp);
    private static IntPtr Find(int pid,string title){IntPtr found=IntPtr.Zero;EnumWindows((window,_)=>{GetWindowThreadProcessId(window,out var owner);if(owner==pid){var text=new StringBuilder(256);GetWindowText(window,text,256);if(text.ToString()==title)found=window;}return true;},IntPtr.Zero);return found;}
    internal static async Task SetupSmokeAsync(string executable)
    {
        executable=Path.GetFullPath(executable);var root=Path.Combine(Path.GetTempPath(),"LingdongCapsule-setup");var before=Directory.Exists(root) ? Directory.GetDirectories(root) : [];
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true};start.Environment["DOTNET_ROOT"]=Path.Combine(Path.GetTempPath(),"Capsule-absent-shared-runtime-"+Guid.NewGuid().ToString("N"));start.Environment["DOTNET_ROOT_X64"]=start.Environment["DOTNET_ROOT"];start.Environment["DOTNET_MULTILEVEL_LOOKUP"]="0";
        using var process=Process.Start(start)!;
        try
        {
            var deadline=Stopwatch.StartNew();var window=IntPtr.Zero;
            while(deadline.Elapsed.TotalSeconds<60 && !process.HasExited){window=Find(process.Id,"安装灵动胶囊");if(window!=IntPtr.Zero)break;await Task.Delay(100);}
            if(window==IntPtr.Zero || process.HasExited)throw new Exception("Standalone setup did not initialize its ready maintenance window");
            var stages=Directory.GetDirectories(root).Except(before).ToArray();if(stages.Length!=1 || !File.Exists(Path.Combine(stages[0],"payload","BUILD-INFO.json")))throw new Exception("Standalone setup did not extract its owned payload");
            PostMessage(window,0x10,IntPtr.Zero,IntPtr.Zero);await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if(process.ExitCode!=0 || Directory.Exists(stages[0]))throw new Exception("Setup cancel failed or payload was retained");
            Console.WriteLine("PASS standalone setup EXE launched with absent shared runtime, reached real maintenance welcome, cancelled without installation and removed own payload");
        }
        finally{if(!process.HasExited){process.Kill();await process.WaitForExitAsync();}}
    }
    internal static async Task RunAsync(string source)
    {
        source=Path.GetFullPath(source);if(!File.Exists(Path.Combine(source,"MaintenanceCleanup.ps1")))throw new Exception("Build the current self-contained package first");
        var root=Path.GetFullPath(Path.Combine("artifacts","maintenance-real-tests",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);var target=Path.Combine(root,"installed");var id=Guid.NewGuid().ToString("N");
        var profile=Path.Combine(Settings.Folder,"settings.json");var hash=File.Exists(profile) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(profile))) : "absent";
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;Design.ApplyTheme(0);
        var phases=new List<string>();
        Task Worker(string script,string[] args,IProgress<MaintenanceProgress> progress)=>MaintenanceProcess.RunAsync(script,args.Concat(new[]{"-TestRegistrationId",id}),new Progress<MaintenanceProgress>(update=>{phases.Add(update.Stage);progress.Report(update);}));
        var install=new MaintenanceWindow(directory:source,runner:Worker){ShowActivated=false};
        Button FindButton(Window window,string label)=>Children<Button>(window).Single(button=>Equals(button.Content,label));
        async Task Settle(MaintenanceWindow window){var watch=Stopwatch.StartNew();while(window.State==MaintenanceState.Working && watch.Elapsed.TotalSeconds<90)await Task.Delay(100);if(window.State!=MaintenanceState.Completed)throw new Exception("Actual maintenance operation failed; state="+window.State+"; "+string.Join(" | ",Children<TextBlock>(window).Select(text=>text.Text)));}
        try
        {
            install.Show();await Task.Delay(50);FindButton(install,"开始安装").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Children<TextBox>(install).Single().Text=target;FindButton(install,"安装").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(install);
            if(!File.Exists(Path.Combine(target,"installed-files.json")) || !phases.Contains("copying") || !phases.Contains("registering"))throw new Exception("Actual install lacked files or real stage events");
        }
        finally{install.Close();}
        File.WriteAllText(Path.Combine(target,"owned-user-file.txt"),"Preserve user-owned content");
        var previous=Directory.Exists(MaintenanceBootstrap.CacheRoot) ? Directory.GetDirectories(MaintenanceBootstrap.CacheRoot) : [];
        using(var original=Process.Start(new ProcessStartInfo(Path.Combine(target,"LingdongCapsule.exe"),"--uninstall"){UseShellExecute=false,CreateNoWindow=true})!)
        {
            try
            {
                await original.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));if(original.ExitCode!=0)throw new Exception("Installed uninstall bootstrap failed");
                var stages=Directory.GetDirectories(MaintenanceBootstrap.CacheRoot).Except(previous).Where(stage=>MaintenanceBootstrap.ReadTarget(stage)==target).ToArray();if(stages.Length!=1)throw new Exception("Uninstall did not stage exactly one identity-matched helper");var stage=stages[0];
                Process? helper=null;var watch=Stopwatch.StartNew();IntPtr window=IntPtr.Zero;
                while(watch.Elapsed.TotalSeconds<20)
                {
                    foreach(var candidate in Process.GetProcessesByName("LingdongCapsule")){try{if(candidate.MainModule?.FileName==Path.Combine(stage,"LingdongCapsule.exe")){helper=candidate;break;}}catch(System.ComponentModel.Win32Exception){}if(!ReferenceEquals(candidate,helper))candidate.Dispose();}
                    if(helper!=null){window=Find(helper.Id,"卸载灵动胶囊");if(window!=IntPtr.Zero)break;helper.Dispose();helper=null;}
                    await Task.Delay(100);
                }
                if(helper==null || window==IntPtr.Zero)throw new Exception("Staged graphical uninstall welcome did not initialize");
                using(helper){PostMessage(window,0x10,IntPtr.Zero,IntPtr.Zero);await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));if(helper.ExitCode!=0)throw new Exception("Uninstall cancel failed");}
                for(var attempt=0;attempt<100 && Directory.Exists(stage);attempt++)await Task.Delay(100);
                if(Directory.Exists(stage) || !File.Exists(Path.Combine(target,"LingdongCapsule.exe")))throw new Exception("Cancel removed installed app or failed helper cleanup");
            }
            finally{if(!original.HasExited){original.Kill();await original.WaitForExitAsync();}}
        }
        var uninstall=new MaintenanceWindow(true,target,Worker){ShowActivated=false};
        try{uninstall.Show();await Task.Delay(50);FindButton(uninstall,"确认卸载").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Settle(uninstall);if(File.Exists(Path.Combine(target,"LingdongCapsule.exe")) || !File.Exists(Path.Combine(target,"owned-user-file.txt")) || !phases.Contains("removing"))throw new Exception("Actual graphical uninstall failed its owned-file boundary");}
        finally{uninstall.Close();}
        var after=File.Exists(profile) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(profile))) : "absent";if(hash!=after)throw new Exception("Daily settings changed");
        Console.WriteLine("PASS real graphical install stages, installed uninstall bootstrap, staged welcome/cancel/cleanup, actual GUI worker uninstall, own GUID registration and preserved user files/settings; evidence: "+root);
    }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject{for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var nested in Children<T>(child))yield return nested;}}
}
