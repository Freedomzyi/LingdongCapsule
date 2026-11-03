using Capsule.PluginApi;
using System.IO;
using System.Windows;

namespace Capsule.App;

public partial class MainWindow
{
    private PluginRuntime plugins=null!;
    private string pluginDirectory="";
    internal System.Windows.Controls.StackPanel LivePluginView=>plugins.View;
    private readonly Dictionary<string,string> pluginPaths=new(StringComparer.OrdinalIgnoreCase);
    private uint pluginNoticeId;
    private void InitializePlugins(bool persist)
    {
        plugins=new(settings,()=>new(state.Media.Title,state.Media.Artist,state.Media.Available,state.Media.Playing),
            (id,name,title,body)=>Dispatcher.InvokeAsync(()=>!closing && AcceptNotification(new("plugin:"+id,name,title,body,++pluginNoticeId),DateTimeOffset.Now)).Task,
            ()=>{if(persist)settings.Save();});
        plugins.View.Margin=new Thickness(12,0,0,0);plugins.View.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"IslandInk");Clock.Children.Add(plugins.View);
        Loaded+=(_,_)=>{foreach(var file in settings.PluginFiles.ToArray())try{LoadPlugin(file,false);}catch(Exception error){Design.PresentMessage("插件未能启用",Path.GetFileName(file)+"\n"+error.Message);}};
        Closed+=(_,_)=>plugins.Dispose();
    }
    internal void LoadPlugin(string file,bool save=true)
    {
        var path=Path.GetFullPath(file);PluginPackageInfo? expected=null;
        if(path.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)){var installed=PluginPackage.Install(path,pluginDirectory);path=installed.EntryFile;expected=installed.Info;}
        var id=plugins.Enable(path);
        if(expected!=null && id!=expected.Id){plugins.Disable(id);throw new InvalidDataException("清单 ID 与插件实际 ID 不一致，未启用。");}
        pluginPaths[id]=path;
        if(!settings.PluginFiles.Contains(path,StringComparer.OrdinalIgnoreCase))settings.PluginFiles.Add(path);
        if(save)SaveAndApplySettings();else Render();
    }
    private void DisablePlugin(string id)
    {
        plugins.Disable(id);if(pluginPaths.Remove(id,out var path))settings.PluginFiles.RemoveAll(file=>string.Equals(file,path,StringComparison.OrdinalIgnoreCase));SaveAndApplySettings();
    }
    private void MovePlugin(string id,int direction)
    {
        if(!plugins.Move(id,direction))return;
        settings.PluginFiles=plugins.ActiveIds.Select(item=>pluginPaths[item]).Concat(settings.PluginFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToList();SaveAndApplySettings();
    }
}
