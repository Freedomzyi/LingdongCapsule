using Capsule.PluginApi;
using System.IO;
using System.Windows;

namespace Capsule.App;

public partial class MainWindow
{
    private PluginRuntime plugins=null!;
    private string pluginDirectory="";
    internal IReadOnlyList<FrameworkElement> LivePluginWidgets=>plugins.ActiveIds.Select(plugins.Widget).ToArray();
    private readonly Dictionary<string,string> pluginPaths=new(StringComparer.OrdinalIgnoreCase);
    private uint pluginNoticeId;
    private bool persistPluginState;
    private readonly Action writePluginSettings;
    private int pluginMutationDepth;
    internal PluginCatalogEntry[] InstalledPlugins()=>PluginCatalog.Read(pluginDirectory,settings.PluginFiles,settings.PendingPluginRemovals);
    internal bool InstallMarketPackage(string file,string id)
    {
        var previous=InstalledPlugins().Where(item=>item.Id==id && !item.PendingRemoval && item.Error.Length==0).ToArray();
        if(pluginPaths.ContainsKey(id) || previous.Length==0){LoadPlugin(file,expectedMarketId:id);return true;}
        // An update of a stopped component validates identity without creating
        // its widget or adding it to the persisted enabled list.
        var oldPending=settings.PendingPluginRemovals.ToList();InstalledPlugin? installed=null;pluginMutationDepth++;
        try
        {
            installed=PluginPackage.Install(file,pluginDirectory);
            using(var module=new PluginModule(installed.EntryFile))
            {
                var actual=module.Plugin!.Identity;
                if(installed.Info.Id!=id || actual.Id!=id || PluginMarket.VersionOf(actual.Version)!=PluginMarket.VersionOf(installed.Info.Version))throw new InvalidDataException("市场包清单与实际组件不一致。");
                if(previous.Any(item=>PluginMarket.VersionOf(item.Version)>PluginMarket.VersionOf(actual.Version)))throw new InvalidOperationException("市场更新不能降级已安装组件。");
            }
            foreach(var item in previous)if(!settings.PendingPluginRemovals.Contains(item.EntryFile,StringComparer.OrdinalIgnoreCase))settings.PendingPluginRemovals.Add(item.EntryFile);
            writePluginSettings();
        }
        catch
        {
            settings.PendingPluginRemovals=oldPending;if(installed!=null)CleanupPluginPackage(installed.EntryFile,true);throw;
        }
        finally{pluginMutationDepth--;}
        foreach(var item in previous)CleanupPluginPackage(item.EntryFile,true);
        Render();return false;
    }
    private void InitializePlugins(bool persist)
    {
        persistPluginState=persist;
        plugins=new(settings,()=>new(state.Media.Title,state.Media.Artist,state.Media.Available,state.Media.Playing),
            (id,name,title,body)=>pluginMutationDepth>0 ? Task.FromResult(false) : Dispatcher.CheckAccess() ? Task.FromResult(!closing && AcceptNotification(new("plugin:"+id,name,title,body,++pluginNoticeId),DateTimeOffset.Now)) : Dispatcher.InvokeAsync(()=>!closing && AcceptNotification(new("plugin:"+id,name,title,body,++pluginNoticeId),DateTimeOffset.Now)).Task,
            ()=>{if(persist && pluginMutationDepth==0)writePluginSettings();});
        plugins.View.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"IslandInk");
        media.TimelineReader=plugins.ReadTimeline;
        Loaded+=(_,_)=>
        {
            var failed=false;
            foreach(var entry in settings.PendingPluginRemovals.ToArray())
            {
                try{PluginPackage.Uninstall(entry,pluginDirectory);settings.PendingPluginRemovals.Remove(entry);failed=true;}
                catch(Exception error) when(error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException){settings.PluginFailures[entry]="待删除插件仍未清理："+error.GetType().Name;}
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
    internal void LoadPlugin(string file,bool save=true,string? expectedMarketId=null)
    {
        var oldFiles=settings.PluginFiles.ToList();var oldPaths=new Dictionary<string,string>(pluginPaths,StringComparer.OrdinalIgnoreCase);
        var oldOrder=settings.ComponentOrder.ToList();var oldRuntimeOrder=plugins.ActiveIds.ToArray();
        var oldFailures=new Dictionary<string,string>(settings.PluginFailures);var oldPending=settings.PendingPluginRemovals.ToList();
        var oldPreferences=settings.PluginPreferences.ToDictionary(item=>item.Key,item=>new Dictionary<string,string>(item.Value));
        var path=Path.GetFullPath(file);PluginPackageInfo? expected=null;
        string? previousPath=null,enabledId=null;var previousIndex=-1;var oldDisabled=false;var installedPackage=false;
        pluginMutationDepth++;
        try
        {
            if(path.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)){var installed=PluginPackage.Install(path,pluginDirectory);path=installed.EntryFile;expected=installed.Info;installedPackage=true;}
            else if(path.StartsWith(Path.GetFullPath(pluginDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            {
                var installed=PluginCatalog.Inspect(path,pluginDirectory);
                if(!string.Equals(installed.EntryFile,path,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("选择的文件不是安装清单指定入口。");
                if(settings.PendingPluginRemovals.Contains(path,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("此安装包正在等待清理，请重新安装可信包。");
                expected=installed.Info;
            }
            if(expectedMarketId!=null && expected?.Id!=expectedMarketId)throw new InvalidDataException("安装包与选择的市场组件不一致。");
            if(expectedMarketId!=null)
            {
                var next=PluginMarket.VersionOf(expected!.Version);
                if(plugins.ActiveIds.Contains(expectedMarketId) && PluginMarket.VersionOf(plugins.Identity(expectedMarketId).Version)>next || InstalledPlugins().Any(item=>item.Id==expectedMarketId && item.Error.Length==0 && !item.PendingRemoval && PluginMarket.VersionOf(item.Version)>next))throw new InvalidOperationException("已安装更高版本，市场更新不能降级组件。");
            }
            if(expected!=null && pluginPaths.TryGetValue(expected.Id,out previousPath)){previousIndex=plugins.ActiveIds.ToList().IndexOf(expected.Id);plugins.Disable(expected.Id);oldDisabled=true;}
            var id=plugins.Enable(path);enabledId=id;
            if(expected!=null)
            {
                static Version NormalizeVersion(string value){var v=Version.Parse(value);return new(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision));}
                if(id!=expected.Id || NormalizeVersion(plugins.Identity(id).Version)!=NormalizeVersion(expected.Version))throw new InvalidDataException("清单与插件实际 ID 或版本不一致，未启用。");
            }
            pluginPaths[id]=path;
            if(previousPath!=null){settings.PluginFiles.RemoveAll(file=>string.Equals(file,previousPath,StringComparison.OrdinalIgnoreCase));for(var index=plugins.ActiveIds.Count-1;index>previousIndex;index--)plugins.Move(id,-1);}
            settings.PluginFailures.Remove(id);settings.PluginFailures.Remove(path);
            settings.ComponentOrder=Capsule.Core.ComponentOrder.Effective(settings.ComponentOrder,plugins.ActiveIds,settings.HardwareFirst,settings.MediaFirst).ToList();
            SetRuntimePluginOrder(settings.ComponentOrder.Where(item=>item.StartsWith("plugin:")).Select(item=>item[7..]));
            settings.PluginFiles=plugins.ActiveIds.Select(item=>pluginPaths[item]).ToList();
            // Persist obsolete-package cleanup before deleting anything. If the
            // second persistence fails, startup can still finish the cleanup.
            if(previousPath!=null && PluginPackage.ManagedDirectory(previousPath,pluginDirectory)!=null && !settings.PendingPluginRemovals.Contains(previousPath,StringComparer.OrdinalIgnoreCase))settings.PendingPluginRemovals.Add(previousPath);
            if(save)writePluginSettings();
        }
        catch(Exception failure)
        {
            if(enabledId!=null)plugins.Disable(enabledId);
            settings.PluginFiles=oldFiles;settings.ComponentOrder=oldOrder;settings.PluginFailures=oldFailures;settings.PendingPluginRemovals=oldPending;settings.PluginPreferences=oldPreferences.ToDictionary(item=>item.Key,item=>new Dictionary<string,string>(item.Value));
            pluginPaths.Clear();foreach(var item in oldPaths)pluginPaths[item.Key]=item.Value;
            Exception? restoreError=null;
            if(oldDisabled && previousPath!=null)
            {
                try{plugins.Enable(previousPath);for(var index=plugins.ActiveIds.Count-1;index>previousIndex;index--)plugins.Move(expected!.Id,-1);}
                catch(Exception error){restoreError=error;pluginPaths.Remove(expected!.Id);settings.PluginFiles.RemoveAll(item=>string.Equals(item,previousPath,StringComparison.OrdinalIgnoreCase));settings.PluginFailures[expected.Id]="旧插件恢复失败："+error.GetType().Name;}
            }
            // A restored plugin may initialize preferences; rollback restores
            // the exact prior preference map, including removed/added keys.
            settings.PluginPreferences=oldPreferences.ToDictionary(item=>item.Key,item=>new Dictionary<string,string>(item.Value));
            SetRuntimePluginOrder(oldRuntimeOrder);
            if(installedPackage)CleanupPluginPackage(path,save);
            Render();
            if(restoreError!=null){if(save)SavePluginRecovery();throw new AggregateException("安装失败，且旧插件未能恢复；已停用故障项。",failure,restoreError);}
            throw;
        }
        finally{pluginMutationDepth--;}
        if(previousPath!=null)CleanupPluginPackage(previousPath,save);
        Render();
    }
    private void CleanupPluginPackage(string path,bool save)
    {
        try
        {
            PluginPackage.Uninstall(path,pluginDirectory);settings.PendingPluginRemovals.RemoveAll(item=>string.Equals(item,path,StringComparison.OrdinalIgnoreCase));settings.PluginFailures.Remove(path);
        }
        catch(Exception error) when(error is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        {
            if(!settings.PendingPluginRemovals.Contains(path,StringComparer.OrdinalIgnoreCase))settings.PendingPluginRemovals.Add(path);
            settings.PluginFailures[path]="包清理待重试："+error.GetType().Name;
        }
        if(save && persistPluginState)SavePluginRecovery();
    }
    private void QuarantineFailedPlugins()
    {
        var changed=false;
        foreach(var entry in pluginPaths.ToArray())
        {
            if(plugins.ActiveIds.Contains(entry.Key) || !plugins.Errors.TryGetValue(entry.Key,out var error))continue;
            settings.PluginFailures[entry.Key]=error;settings.PluginFiles.RemoveAll(file=>string.Equals(file,entry.Value,StringComparison.OrdinalIgnoreCase));pluginPaths.Remove(entry.Key);changed=true;
        }
        if(changed){SavePluginRecovery();Render();}
    }
    private void SavePluginRecovery()
    {
        if(!persistPluginState)return;
        try{writePluginSettings();}catch(Exception error) when(error is IOException or UnauthorizedAccessException){plugins.Errors["settings"]="插件停用状态未能保存，请检查配置目录权限。";}
    }
    internal void DisablePlugin(string id)
    {
        if(!pluginPaths.TryGetValue(id,out var path))return;var old=settings.PluginFiles.ToList();
        settings.PluginFiles.RemoveAll(file=>string.Equals(file,path,StringComparison.OrdinalIgnoreCase));
        try{writePluginSettings();}catch{settings.PluginFiles=old;throw;}
        plugins.Disable(id);pluginPaths.Remove(id);Render();
    }
    internal void MovePlugin(string id,int direction)
    {
        var ids=plugins.ActiveIds.ToArray();var index=Array.IndexOf(ids,id);if(index<0)return;var target=Math.Clamp(index+Math.Sign(direction),0,ids.Length-1);if(index==target)return;
        var full=EffectiveModuleOrder().ToList();var first=full.IndexOf("plugin:"+id);var second=full.IndexOf("plugin:"+ids[target]);(full[first],full[second])=(full[second],full[first]);CommitDisplayOrder(full);
    }
    internal void UninstallPlugin(string id)
    {
        if(!pluginPaths.TryGetValue(id,out var path))return;
        var oldFiles=settings.PluginFiles.ToList();var oldPending=settings.PendingPluginRemovals.ToList();
        settings.PluginFiles.RemoveAll(file=>string.Equals(file,path,StringComparison.OrdinalIgnoreCase));
        if(PluginPackage.ManagedDirectory(path,pluginDirectory)!=null && !settings.PendingPluginRemovals.Contains(path,StringComparer.OrdinalIgnoreCase))settings.PendingPluginRemovals.Add(path);
        try{writePluginSettings();}catch{settings.PluginFiles=oldFiles;settings.PendingPluginRemovals=oldPending;throw;}
        plugins.Disable(id);pluginPaths.Remove(id);CleanupPluginPackage(path,true);Render();
    }
    internal void RemoveInstalledPlugin(string entry)
    {
        var active=pluginPaths.FirstOrDefault(item=>string.Equals(item.Value,entry,StringComparison.OrdinalIgnoreCase));
        if(active.Key!=null){UninstallPlugin(active.Key);return;}
        if(PluginPackage.ManagedDirectory(entry,pluginDirectory)==null)throw new InvalidDataException("不能删除没有匹配安装凭据的目录。");
        var old=settings.PendingPluginRemovals.ToList();
        if(!settings.PendingPluginRemovals.Contains(entry,StringComparer.OrdinalIgnoreCase))settings.PendingPluginRemovals.Add(entry);
        try{writePluginSettings();}catch{settings.PendingPluginRemovals=old;throw;}
        CleanupPluginPackage(entry,true);Render();
    }
}
