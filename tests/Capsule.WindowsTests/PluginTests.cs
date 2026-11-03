using Capsule.App;
using Capsule.PluginApi;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.IO.Compression;

public sealed class FixturePlugin : ICapsulePlugin
{
    public PluginIdentity Identity=>new(System.IO.Path.GetFileName(typeof(FixturePlugin).Assembly.Location).Contains("second",StringComparison.OrdinalIgnoreCase) ? "fixture.second" : "fixture.clock","Test clock","0.1.0");
    public bool Disposed{get;private set;}
    private readonly TextBlock view=new();
    private ICapsulePluginHost? host;
    public FrameworkElement CreateWidget(ICapsulePluginHost host){this.host=host;view.Text=host.ReadSetting("label","Test");return view;}
    public void Update(PluginFrame frame){if(frame.Media.Title=="throw" || host?.ReadSetting("fail")=="true")throw new InvalidOperationException("Fixture callback error");view.Text=frame.Now.ToString("HH:mm");}
    public void Dispose()=>Disposed=true;
}

internal static class PluginTests
{
    private sealed class Host : ICapsulePluginHost
    {
        public PluginMedia CurrentMedia=>new("","",false,false);
        public string ReadSetting(string key,string fallback="")=>fallback;
        public void WriteSetting(string key,string value){ }
        public Task<bool> NotifyAsync(string title,string body)=>Task.FromResult(true);
    }
    internal static async Task RunAsync()
    {
        var packageRoot=Path.Combine(Environment.CurrentDirectory,"artifacts","plugin-package-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(packageRoot);
        var valid=Path.Combine(packageRoot,"valid.zip");
        using(var zip=ZipFile.Open(valid,ZipArchiveMode.Create)){using(var writer=new StreamWriter(zip.CreateEntry("plugin.json").Open()))writer.Write("{\"id\":\"fixture.clock\",\"name\":\"Test\",\"version\":\"0.1.0\",\"entry\":\"fixture.dll\"}");zip.CreateEntryFromFile(typeof(FixturePlugin).Assembly.Location,"fixture.dll");}
        var installed=PluginPackage.Install(valid,Path.Combine(packageRoot,"installed"));
        var removable=PluginPackage.Install(valid,Path.Combine(packageRoot,"removable"));
        if(!PluginPackage.Uninstall(removable.EntryFile,Path.Combine(packageRoot,"removable")) || Directory.Exists(removable.Directory))throw new Exception("Managed package uninstall failed");
        if(PluginPackage.Uninstall(typeof(FixturePlugin).Assembly.Location,Path.Combine(packageRoot,"removable")) || !File.Exists(typeof(FixturePlugin).Assembly.Location))throw new Exception("External plugin file was deleted");
        var locked=PluginPackage.Install(valid,Path.Combine(packageRoot,"locked"));var blocked=false;
        using(var hold=new FileStream(locked.EntryFile,FileMode.Open,FileAccess.Read,FileShare.None)){try{PluginPackage.Uninstall(locked.EntryFile,Path.Combine(packageRoot,"locked"));}catch(IOException){blocked=true;}}
        if(!blocked || !File.Exists(Path.Combine(locked.Directory,".capsule-install.json")) || !PluginPackage.Uninstall(locked.EntryFile,Path.Combine(packageRoot,"locked")))throw new Exception("Occupied package did not preserve receipt for retry");
        using(var loaded=new PluginModule(installed.EntryFile))if(loaded.Plugin?.Identity.Id!="fixture.clock")throw new Exception("Installed package did not load independently");
        var unsafePackage=Path.Combine(packageRoot,"unsafe.zip");using(var zip=ZipFile.Open(unsafePackage,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("../escape.txt").Open());writer.Write("must not escape");}
        var rejected=false;try{PluginPackage.Install(unsafePackage,Path.Combine(packageRoot,"unsafe-target"));}catch(InvalidDataException){rejected=true;}
        if(!rejected || File.Exists(Path.Combine(packageRoot,"escape.txt")))throw new Exception("Traversal package was not rejected");
        Console.WriteLine("PASS Plugin package installation and traversal rejection");
        using var module=new PluginModule(typeof(FixturePlugin).Assembly.Location);
        var plugin=module.Plugin!;var view=plugin.CreateWidget(new Host());
        plugin.Update(new(new DateTimeOffset(2026,11,3,12,34,0,TimeSpan.Zero),true,new("","",false,false)));
        if(plugin.Identity.Id!="fixture.clock" || ((TextBlock)view).Text!="12:34")throw new Exception("Plugin contract or widget update failed");
        module.Dispose();if(module.Plugin!=null || plugin.GetType().GetProperty("Disposed")!.GetValue(plugin) is not true)throw new Exception("Plugin disposal was not called");
        using var runtime=new PluginRuntime(new Settings(),()=>new("","",false,false),(_,_,_,_)=>Task.FromResult(true),()=>{});
        var id=runtime.Enable(typeof(FixturePlugin).Assembly.Location);
        runtime.Update(new(new DateTimeOffset(2026,11,3,10,12,0,TimeSpan.Zero),true,new("","",false,false)));
        if(runtime.View.Children.Count!=1 || ((TextBlock)runtime.View.Children[0]).Text!="10:12")throw new Exception("Runtime did not host plugin widget");
        var secondFolder=System.IO.Path.Combine(Environment.CurrentDirectory,"artifacts","plugin-tests",Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(secondFolder);var secondFile=System.IO.Path.Combine(secondFolder,"fixture.second.dll");System.IO.File.Copy(typeof(FixturePlugin).Assembly.Location,secondFile);
        var secondId=runtime.Enable(secondFile);var secondView=runtime.View.Children[1];
        if(!runtime.Move(secondId,-1) || runtime.ActiveIds.First()!=secondId || !ReferenceEquals(runtime.View.Children[0],secondView))throw new Exception("Plugin order did not match visual order");
        runtime.Disable(secondId);
        runtime.Disable(id);if(runtime.View.Children.Count!=0 || runtime.ActiveIds.Count!=0)throw new Exception("Runtime did not remove disabled widget");
        runtime.Enable(typeof(FixturePlugin).Assembly.Location);runtime.Update(new(DateTimeOffset.Now,true,new("throw","",true,true)));
        if(runtime.ActiveIds.Count!=0 || !runtime.Errors.ContainsKey(id))throw new Exception("Plugin callback failure was not isolated");
        Console.WriteLine("PASS Independent plugin assembly loading, shared API, widget update and disposal");
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();Design.ApplySystemTheme();
        var windowSettings=new Settings{MediaEnabled=false,NotificationsEnabled=false};
        var missing=Path.Combine(packageRoot,"missing.dll");windowSettings.PluginFiles.Add(missing);
        var window=new Capsule.App.MainWindow(suppressOnboarding:true,initialSettings:windowSettings,localPluginDirectory:Path.Combine(packageRoot,"window-installed"));
        try
        {
            window.Show();await Task.Delay(300);window.LoadPlugin(valid,save:false);
            if(!windowSettings.PluginFailures.ContainsKey(missing) || windowSettings.PluginFiles.Contains(missing))throw new Exception("Failed startup plugin was not quarantined");
            if(windowSettings.PluginFiles.Count!=1 || !windowSettings.PluginFiles[0].EndsWith("fixture.dll"))throw new Exception("Installed plugin entry was not retained for restart");
            for(var attempt=0;attempt<30 && ((TextBlock)window.LivePluginView.Children[0]).Text=="Test";attempt++)await Task.Delay(100);
            var text=(TextBlock)window.LivePluginView.Children[0];
            if(text.Text.Length!=5 || !text.Text.Contains(':') || !text.IsVisible)throw new Exception("Actual island plugin did not display live clock");
            window.LoadPlugin(valid,save:false);var retained=windowSettings.PluginFiles.Single();
            var mismatch=Path.Combine(packageRoot,"version-mismatch.zip");
            using(var zip=ZipFile.Open(mismatch,ZipArchiveMode.Create)){using(var writer=new StreamWriter(zip.CreateEntry("plugin.json").Open()))writer.Write("{\"id\":\"fixture.clock\",\"name\":\"Test\",\"version\":\"0.2.0\",\"entry\":\"fixture.dll\"}");zip.CreateEntryFromFile(typeof(FixturePlugin).Assembly.Location,"fixture.dll");}
            var mismatchRejected=false;try{window.LoadPlugin(mismatch,save:false);}catch(InvalidDataException){mismatchRejected=true;}
            if(!mismatchRejected || window.LivePluginView.Children.Count!=1 || windowSettings.PluginFiles.Single()!=retained)throw new Exception("Failed plugin upgrade did not restore previous entry");
            windowSettings.PluginPreferences["fixture.clock"]=new(){["fail"]="true"};
            for(var attempt=0;attempt<30 && window.LivePluginView.Children.Count!=0;attempt++)await Task.Delay(100);
            if(window.LivePluginView.Children.Count!=0 || windowSettings.PluginFiles.Count!=0 || !windowSettings.PluginFailures.ContainsKey("fixture.clock"))throw new Exception("Faulted plugin was not removed from restart list");
            Console.WriteLine("PASS Actual island hosts and updates an independently loaded plugin widget");
        }
        finally{window.Close();}
    }
}
