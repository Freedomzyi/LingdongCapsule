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
    public void Update(PluginFrame frame)=>view.Text=frame.Now.ToString("HH:mm");
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
    internal static void Run()
    {
        using var module=new PluginModule(typeof(FixturePlugin).Assembly.Location);
        var plugin=module.Plugin!;var view=plugin.CreateWidget(new Host());
        plugin.Update(new(new DateTimeOffset(2026,11,3,12,34,0,TimeSpan.Zero),true,new("","",false,false)));
        if(plugin.Identity.Id!="fixture.clock" || ((TextBlock)view).Text!="12:34")throw new Exception("Plugin contract or widget update failed");
        module.Dispose();if(module.Plugin!=null || plugin.GetType().GetProperty("Disposed")!.GetValue(plugin) is not true)throw new Exception("Plugin disposal was not called");
        Console.WriteLine("PASS Independent plugin assembly loading, shared API, widget update and disposal");
    }
}
