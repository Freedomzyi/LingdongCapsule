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
    public FrameworkElement CreateWidget(ICapsulePluginHost host){this.host=host;view.Text=host.ReadSetting("label","Test");host.WriteSetting("initializations",(int.Parse(host.ReadSetting("initializations","0"))+1).ToString());return view;}
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
        PluginPackageTests.Run(packageRoot,typeof(FixturePlugin).Assembly.Location);
        var samplePackage=Path.Combine(Environment.CurrentDirectory,"artifacts","plugins","capsule.centerclock-0.1.0.zip");
        if(!File.Exists(samplePackage))throw new Exception("Build the real plugin package first: scripts/build-plugins.ps1");
        var sampleInstalled=PluginPackage.Install(samplePackage,Path.Combine(packageRoot,"sample-installed"));
        using(var sampleModule=new PluginModule(sampleInstalled.EntryFile))
        {
            var sample=sampleModule.Plugin!;var sampleView=(TextBlock)sample.CreateWidget(new Host());sample.Update(new(new DateTimeOffset(2026,11,3,12,34,0,TimeSpan.Zero),true,new("","",false,false)));
            if(sample.Identity.Id!="capsule.centerclock" || sampleView.Text!="12:34" || ((System.Windows.Media.SolidColorBrush)sampleView.Foreground).Color!=System.Windows.Media.Colors.White)throw new Exception("Built sample package API/time/theme failed");
            sample.Update(new(new DateTimeOffset(2026,11,3,12,35,0,TimeSpan.Zero),false,new("","",false,false)));
            if(sampleView.Text!="12:35" || ((System.Windows.Media.SolidColorBrush)sampleView.Foreground).Color.R>100)throw new Exception("Built sample package light theme failed");
        }
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
        var windowSettings=new Settings{MediaEnabled=false,NotificationsEnabled=false,ClockEnabled=false};
        var missing=Path.Combine(packageRoot,"missing.dll");windowSettings.PluginFiles.Add(missing);
        var failSave=false;var saved="";var writes=0;
        var window=new Capsule.App.MainWindow(suppressOnboarding:true,initialSettings:windowSettings,localPluginDirectory:Path.Combine(packageRoot,"window-installed"),pluginSettingsWriter:()=>{writes++;if(failSave)throw new IOException("Owned simulated save failure");saved=System.Text.Json.JsonSerializer.Serialize(windowSettings);});
        try
        {
            window.Show();await Task.Delay(300);window.LoadPlugin(valid,save:false);
            if(!windowSettings.PluginFailures.ContainsKey(missing) || windowSettings.PluginFiles.Contains(missing))throw new Exception("Failed startup plugin was not quarantined");
            if(windowSettings.PluginFiles.Count!=1 || !windowSettings.PluginFiles[0].EndsWith("fixture.dll"))throw new Exception("Installed plugin entry was not retained for restart");
            for(var attempt=0;attempt<30 && ((TextBlock)window.LivePluginView.Children[0]).Text=="Test";attempt++)await Task.Delay(100);
            var text=(TextBlock)window.LivePluginView.Children[0];
            if(text.Text.Length!=5 || !text.Text.Contains(':') || !text.IsVisible)throw new Exception("Actual island plugin did not display live clock");
            window.UpdateLayout();var center=text.TransformToAncestor(window).Transform(new Point(text.ActualWidth/2,text.ActualHeight/2));
            if(Math.Abs(center.X-window.ActualWidth/2)>1)throw new Exception("Standalone plugin clock is offset by hidden module spacing");
            window.LoadPlugin(valid,save:false);var retained=windowSettings.PluginFiles.Single();
            var mismatch=Path.Combine(packageRoot,"version-mismatch.zip");
            using(var zip=ZipFile.Open(mismatch,ZipArchiveMode.Create)){using(var writer=new StreamWriter(zip.CreateEntry("plugin.json").Open()))writer.Write("{\"id\":\"fixture.clock\",\"name\":\"Test\",\"version\":\"0.2.0\",\"entry\":\"fixture.dll\"}");zip.CreateEntryFromFile(typeof(FixturePlugin).Assembly.Location,"fixture.dll");}
            var mismatchRejected=false;try{window.LoadPlugin(mismatch,save:false);}catch(InvalidDataException){mismatchRejected=true;}
            if(!mismatchRejected || window.LivePluginView.Children.Count!=1 || windowSettings.PluginFiles.Single()!=retained)throw new Exception("Failed plugin upgrade did not restore previous entry");
            var preferences=System.Text.Json.JsonSerializer.Serialize(windowSettings.PluginPreferences);
            failSave=true;var failed=false;try{window.LoadPlugin(valid);}catch(IOException){failed=true;}
            if(!failed || windowSettings.PluginFiles.Single()!=retained || window.LivePluginView.Children.Count!=1 || System.Text.Json.JsonSerializer.Serialize(windowSettings.PluginPreferences)!=preferences || saved.Length!=0 || writes!=1)throw new Exception("Save failure did not restore old plugin, restart path and preferences");
            failed=false;try{window.DisablePlugin("fixture.clock");}catch(IOException){failed=true;}
            if(!failed || windowSettings.PluginFiles.Single()!=retained || window.LivePluginView.Children.Count!=1)throw new Exception("Failed disable changed live plugin");
            var pending=windowSettings.PendingPluginRemovals.ToArray();failed=false;try{window.UninstallPlugin("fixture.clock");}catch(IOException){failed=true;}
            if(!failed || windowSettings.PluginFiles.Single()!=retained || window.LivePluginView.Children.Count!=1 || !windowSettings.PendingPluginRemovals.SequenceEqual(pending) || !File.Exists(retained))throw new Exception("Failed uninstall changed live plugin or removed files");
            failSave=false;window.LoadPlugin(valid);retained=windowSettings.PluginFiles.Single();
            var persisted=System.Text.Json.JsonSerializer.Deserialize<Settings>(saved)!;
            if(persisted.PluginFiles.Single()!=retained || window.LivePluginView.Children.Count!=1 || !File.Exists(retained))throw new Exception("Successful upgrade did not commit live and saved entry together");
            var secondPackage=Path.Combine(packageRoot,"second-valid.zip");
            using(var zip=ZipFile.Open(secondPackage,ZipArchiveMode.Create)){using(var writer=new StreamWriter(zip.CreateEntry("plugin.json").Open()))writer.Write("{\"id\":\"fixture.second\",\"name\":\"Second\",\"version\":\"0.1.0\",\"entry\":\"fixture-second.dll\"}");zip.CreateEntryFromFile(typeof(FixturePlugin).Assembly.Location,"fixture-second.dll");}
            preferences=System.Text.Json.JsonSerializer.Serialize(windowSettings.PluginPreferences);failSave=true;failed=false;
            try{window.LoadPlugin(secondPackage);}catch(IOException){failed=true;}
            if(!failed || window.LivePluginView.Children.Count!=1 || windowSettings.PluginFiles.Single()!=retained || System.Text.Json.JsonSerializer.Serialize(windowSettings.PluginPreferences)!=preferences)throw new Exception("First install save failure retained new widget or preferences");
            failSave=false;window.LoadPlugin(secondPackage);var filesBeforeMove=windowSettings.PluginFiles.ToArray();var childrenBeforeMove=window.LivePluginView.Children.Cast<UIElement>().ToArray();
            failSave=true;failed=false;try{window.MovePlugin("fixture.second",-1);}catch(IOException){failed=true;}
            if(!failed || !windowSettings.PluginFiles.SequenceEqual(filesBeforeMove) || !window.LivePluginView.Children.Cast<UIElement>().SequenceEqual(childrenBeforeMove))throw new Exception("Failed reorder did not restore view and saved order");
            failSave=false;window.MovePlugin("fixture.second",-1);persisted=System.Text.Json.JsonSerializer.Deserialize<Settings>(saved)!;
            if(window.LivePluginView.Children[0]!=childrenBeforeMove[1] || !persisted.PluginFiles.SequenceEqual(filesBeforeMove.Reverse()))throw new Exception("Reorder did not commit view and restart order");
            var stopped=windowSettings.PluginFiles[0];window.DisablePlugin("fixture.second");
            if(window.LivePluginView.Children.Count!=1 || windowSettings.PluginFiles.Single()!=retained || !File.Exists(stopped))throw new Exception("Stop removed package files or retained active widget");
            var catalog=window.InstalledPlugins();var stoppedRow=catalog.Single(item=>item.EntryFile==stopped);
            if(!stoppedRow.CanEnable || stoppedRow.Name!="Second" || catalog.Single(item=>item.EntryFile==retained).CanEnable)throw new Exception("Installed catalog active/stopped state failed");
            window.LoadPlugin(stopped);if(window.LivePluginView.Children.Count!=2 || !windowSettings.PluginFiles.Contains(stopped))throw new Exception("Stopped installed package could not be re-enabled by entry");
            window.DisablePlugin("fixture.second");var pendingBeforeRemoval=windowSettings.PendingPluginRemovals.ToArray();failSave=true;failed=false;
            var stoppedManifest=Path.Combine(Path.GetDirectoryName(stopped)!,"plugin.json");var originalManifest=File.ReadAllText(stoppedManifest);
            File.WriteAllText(stoppedManifest,originalManifest.Replace("0.1.0","0.2.0"));var wrongVersion=false;
            try{window.LoadPlugin(stopped);}catch(InvalidDataException){wrongVersion=true;}
            if(!wrongVersion || window.LivePluginView.Children.Count!=1 || !File.Exists(stopped))throw new Exception("Installed DLL activation bypassed manifest/version validation");
            File.WriteAllText(stoppedManifest,originalManifest);
            typeof(Capsule.App.MainWindow).GetMethod("OpenSettings",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,null);
            await Task.Delay(150);var settingsWindow=Application.Current.Windows.OfType<SettingsWindow>().Single();
            var navigation=Descendants<Button>(settingsWindow).Single(button=>button.Content as string=="我的插件");navigation.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);settingsWindow.UpdateLayout();
            if(!Descendants<Button>(settingsWindow).Any(button=>button.Content as string=="启用") || !Descendants<TextBlock>(settingsWindow).Any(text=>text.Text=="已安装但未启用"))throw new Exception("Installed package actions missing from actual settings page");
            var settingsImage=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(settingsWindow.ActualWidth),(int)Math.Ceiling(settingsWindow.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);settingsImage.Render(settingsWindow);
            var imageFolder=Path.Combine("artifacts","verification","plugins");Directory.CreateDirectory(imageFolder);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(settingsImage));using(var file=File.Create(Path.Combine(imageFolder,"installed-page.png")))encoder.Save(file);
            settingsWindow.Close();
            try{window.RemoveInstalledPlugin(stopped);}catch(IOException){failed=true;}
            if(!failed || !File.Exists(stopped) || !windowSettings.PendingPluginRemovals.SequenceEqual(pendingBeforeRemoval))throw new Exception("Failed inactive deletion removed files or changed cleanup queue");
            failSave=false;window.RemoveInstalledPlugin(stopped);
            if(File.Exists(stopped) && !windowSettings.PendingPluginRemovals.Contains(stopped))throw new Exception("Inactive package neither deleted nor queued");
            var corrupt=PluginPackage.Install(valid,Path.Combine(packageRoot,"window-installed"));File.WriteAllText(Path.Combine(corrupt.Directory,"plugin.json"),"{broken");
            var corruptRow=window.InstalledPlugins().Single(item=>item.EntryFile==Path.Combine(corrupt.Directory,"plugin.json"));
            if(corruptRow.CanEnable || corruptRow.Error.Length==0)throw new Exception("Corrupt package was presented as executable");
            window.RemoveInstalledPlugin(corruptRow.EntryFile);if(Directory.Exists(corrupt.Directory))throw new Exception("Owned corrupt package could not be removed safely");
            Console.WriteLine("PASS Installed catalog, stopped-package re-enable, save-failed deletion and corrupt-manifest cleanup");
            Console.WriteLine("PASS Save-failed upgrade restores old widget/path/preferences; failed stop/uninstall preserve live state; successful upgrade commits");
            Console.WriteLine("PASS First-install failure leaves no widget/preferences; reorder failure restores live order; successful move/stop persist");
            windowSettings.PluginPreferences["fixture.clock"]=new(){["fail"]="true"};
            for(var attempt=0;attempt<30 && window.LivePluginView.Children.Count!=0;attempt++)await Task.Delay(100);
            if(window.LivePluginView.Children.Count!=0 || windowSettings.PluginFiles.Count!=0 || !windowSettings.PluginFailures.ContainsKey("fixture.clock"))throw new Exception("Faulted plugin was not removed from restart list");
            window.LoadPlugin(samplePackage,save:false);
            for(var attempt=0;attempt<30 && ((TextBlock)window.LivePluginView.Children[0]).Text.Length==0;attempt++)await Task.Delay(100);
            if(((TextBlock)window.LivePluginView.Children[0]).Text!=DateTime.Now.ToString("HH:mm"))throw new Exception("Built sample clock did not render in actual island");
            Console.WriteLine("PASS Built distributable sample plugin time/themes and actual island loading");
            Console.WriteLine("PASS Actual island hosts and updates an independently loaded plugin widget");
        }
        finally{window.Close();}
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var index=0;index<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);index++)
        {
            var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,index);if(child is T found)yield return found;
            foreach(var nested in Descendants<T>(child))yield return nested;
        }
    }
}
