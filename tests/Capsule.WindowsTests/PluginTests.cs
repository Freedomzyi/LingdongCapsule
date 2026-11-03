using Capsule.App;
using Capsule.PluginApi;
using System.Windows;
using System.Windows.Controls;

public sealed class FixturePlugin : ICapsulePlugin
{
    public PluginIdentity Identity=>new("fixture.clock","Test clock","0.1.0");
    public bool Disposed{get;private set;}
    private readonly TextBlock view=new();
    public FrameworkElement CreateWidget(ICapsulePluginHost host){view.Text=host.ReadSetting("label","Test");return view;}
    public void Update(PluginFrame frame){if(frame.Media.Title=="throw")throw new InvalidOperationException("Fixture callback error");view.Text=frame.Now.ToString("HH:mm");}
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
        using var module=new PluginModule(typeof(FixturePlugin).Assembly.Location);
        var plugin=module.Plugin!;var view=plugin.CreateWidget(new Host());
        plugin.Update(new(new DateTimeOffset(2026,11,3,12,34,0,TimeSpan.Zero),true,new("","",false,false)));
        if(plugin.Identity.Id!="fixture.clock" || ((TextBlock)view).Text!="12:34")throw new Exception("Plugin contract or widget update failed");
        module.Dispose();if(module.Plugin!=null || plugin.GetType().GetProperty("Disposed")!.GetValue(plugin) is not true)throw new Exception("Plugin disposal was not called");
        using var runtime=new PluginRuntime(new Settings(),()=>new("","",false,false),(_,_,_,_)=>Task.FromResult(true),()=>{});
        var id=runtime.Enable(typeof(FixturePlugin).Assembly.Location);
        runtime.Update(new(new DateTimeOffset(2026,11,3,10,12,0,TimeSpan.Zero),true,new("","",false,false)));
        if(runtime.View.Children.Count!=1 || ((TextBlock)runtime.View.Children[0]).Text!="10:12")throw new Exception("Runtime did not host plugin widget");
        runtime.Disable(id);if(runtime.View.Children.Count!=0 || runtime.ActiveIds.Count!=0)throw new Exception("Runtime did not remove disabled widget");
        runtime.Enable(typeof(FixturePlugin).Assembly.Location);runtime.Update(new(DateTimeOffset.Now,true,new("throw","",true,true)));
        if(runtime.ActiveIds.Count!=0 || !runtime.Errors.ContainsKey(id))throw new Exception("Plugin callback failure was not isolated");
        Console.WriteLine("PASS Independent plugin assembly loading, shared API, widget update and disposal");
        var app=new Capsule.App.App{SuppressStartupForTests=true};app.InitializeComponent();Design.ApplySystemTheme();
        var window=new Capsule.App.MainWindow(suppressOnboarding:true,initialSettings:new Settings{MediaEnabled=false,NotificationsEnabled=false});
        try
        {
            window.Show();await Task.Delay(300);window.LoadPlugin(typeof(FixturePlugin).Assembly.Location,save:false);
            for(var attempt=0;attempt<30 && ((TextBlock)window.LivePluginView.Children[0]).Text=="Test";attempt++)await Task.Delay(100);
            var text=(TextBlock)window.LivePluginView.Children[0];
            if(text.Text.Length!=5 || !text.Text.Contains(':') || !text.IsVisible)throw new Exception("Actual island plugin did not display live clock");
            Console.WriteLine("PASS Actual island hosts and updates an independently loaded plugin widget");
        }
        finally{window.Close();}
    }
}
