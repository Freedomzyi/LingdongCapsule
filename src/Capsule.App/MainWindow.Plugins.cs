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
    private bool persistPluginState;
    private void InitializePlugins(bool persist)
    {
        persistPluginState=persist;
        plugins=new(settings,()=>new(state.Media.Title,state.Media.Artist,state.Media.Available,state.Media.Playing),
            (id,name,title,body)=>Dispatcher.InvokeAsync(()=>!closing && AcceptNotification(new("plugin:"+id,name,title,body,++pluginNoticeId),DateTimeOffset.Now)).Task,
            ()=>{if(persist)settings.Save();});
        plugins.View.Margin=new Thickness(12,0,0,0);plugins.View.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"IslandInk");Clock.Children.Add(plugins.View);
        Loaded+=(_,_)=>
        {
            var failed=false;
            foreach(var entry in settings.PendingPluginRemovals.ToArray())
            {
                try{PluginPackage.Uninstall(entry,pluginDirectory);settings.PendingPluginRemovals.Remove(entry);failed=true;}
                catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException){settings.PluginFailures[entry]="待删除插件仍未清理："+error.GetType().Name;}
            }
            foreach(var file in settings.PluginFiles.ToArray())try{LoadPlugin(file,false);}catch(Exception error)
            {
                settings.PluginFailures[file]=Path.GetFileName(file)+"：加载失败（"+error.GetType().Name+"）";
                settings.PluginFiles.RemoveAll(item=>string.Equals(item,file,StringComparison.OrdinalIgnoreCase));failed=true;
            }
            if(failed)SavePluginRecovery();
        };
        Closed+=(_,_)=>plugins.Dispose();
    }
    internal void LoadPlugin(string file,bool save=true)
    {
        var path=Path.GetFullPath(file);PluginPackageInfo? expected=null;
        if(path.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)){var installed=PluginPackage.Install(path,pluginDirectory);path=installed.EntryFile;expected=installed.Info;}
        string? previousPath=null;var previousIndex=-1;
        if(expected!=null && pluginPaths.TryGetValue(expected.Id,out previousPath)){previousIndex=plugins.ActiveIds.ToList().IndexOf(expected.Id);plugins.Disable(expected.Id);}
        string id;
        try
        {
            id=plugins.Enable(path);
            if(expected!=null)
            {
                static Version NormalizeVersion(string value){var v=Version.Parse(value);return new(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision));}
                if(id!=expected.Id || NormalizeVersion(plugins.Identity(id).Version)!=NormalizeVersion(expected.Version)){plugins.Disable(id);throw new InvalidDataException("清单与插件实际 ID 或版本不一致，未启用。");}
            }
        }
        catch
        {
            if(previousPath!=null){plugins.Enable(previousPath);for(var index=plugins.ActiveIds.Count-1;index>previousIndex;index--)plugins.Move(expected!.Id,-1);}
            throw;
        }
        if(previousPath!=null){settings.PluginFiles.RemoveAll(file=>string.Equals(file,previousPath,StringComparison.OrdinalIgnoreCase));for(var index=plugins.ActiveIds.Count-1;index>previousIndex;index--)plugins.Move(id,-1);}
        pluginPaths[id]=path;
        settings.PluginFailures.Remove(id);settings.PluginFailures.Remove(path);
        if(!settings.PluginFiles.Contains(path,StringComparer.OrdinalIgnoreCase))settings.PluginFiles.Add(path);
        if(save)SaveAndApplySettings();else Render();
    }
    private void QuarantineFailedPlugins()
    {
        var changed=false;
        foreach(var entry in pluginPaths.ToArray())
        {
            if(plugins.ActiveIds.Contains(entry.Key) || !plugins.Errors.TryGetValue(entry.Key,out var error))continue;
            settings.PluginFailures[entry.Key]=error;settings.PluginFiles.RemoveAll(file=>string.Equals(file,entry.Value,StringComparison.OrdinalIgnoreCase));pluginPaths.Remove(entry.Key);changed=true;
        }
        if(changed)SavePluginRecovery();
    }
    private void SavePluginRecovery()
    {
        if(!persistPluginState)return;
        try{settings.Save();}catch(Exception error) when(error is IOException or UnauthorizedAccessException){plugins.Errors["settings"]="插件停用状态未能保存，请检查配置目录权限。";}
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
    internal void UninstallPlugin(string id)
    {
        if(!pluginPaths.TryGetValue(id,out var path))return;
        plugins.Disable(id);
        settings.PluginFiles.RemoveAll(file=>string.Equals(file,path,StringComparison.OrdinalIgnoreCase));pluginPaths.Remove(id);
        SaveAndApplySettings();
        try{PluginPackage.Uninstall(path,pluginDirectory);}
        catch(IOException){settings.PendingPluginRemovals.Add(path);SavePluginRecovery();Design.PresentMessage("插件已停用","文件仍被占用，将在下次启动时完成删除。个人插件设置继续保留。");}
    }
}
