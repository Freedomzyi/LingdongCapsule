using Capsule.PluginApi;
using System.Windows;
using System.Windows.Controls;

namespace Capsule.App;

internal sealed class PluginRuntime : IDisposable
{
    private sealed record Active(PluginModule Module,FrameworkElement View,PluginIdentity Identity);
    private readonly Dictionary<string,Active> active=new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> order=[];
    private readonly Settings settings;
    private readonly Func<PluginMedia> media;
    private readonly Func<string,string,string,string,Task<bool>> notify;
    private readonly Action save;
    public StackPanel View{get;}=new(){Orientation=Orientation.Horizontal};
    public IReadOnlyCollection<string> ActiveIds=>order.AsReadOnly();
    public PluginIdentity Identity(string id)=>active[id].Identity;
    public FrameworkElement Widget(string id)=>active[id].View;
    public Dictionary<string,string> Errors{get;}=[];
    public PluginRuntime(Settings settings,Func<PluginMedia> media,Func<string,string,string,string,Task<bool>> notify,Action save){this.settings=settings;this.media=media;this.notify=notify;this.save=save;}
    private sealed class Host(PluginRuntime owner,PluginIdentity identity) : ICapsulePluginHost
    {
        public PluginMedia CurrentMedia=>owner.media();
        public string ReadSetting(string key,string fallback="")=>owner.settings.PluginPreferences.TryGetValue(identity.Id,out var values) ? values.GetValueOrDefault(key,fallback) : fallback;
        public void WriteSetting(string key,string value)
        {
            if(string.IsNullOrWhiteSpace(key) || key.Length>80 || value==null || value.Length>8192)throw new ArgumentException("Invalid plugin setting");
            if(!owner.settings.PluginPreferences.TryGetValue(identity.Id,out var values))owner.settings.PluginPreferences[identity.Id]=values=[];
            if(values.Count>=128 && !values.ContainsKey(key))throw new InvalidOperationException("Plugin setting limit reached");
            values[key]=value;owner.save();
        }
        public Task<bool> NotifyAsync(string title,string body)
        {
            if(title==null || body==null || title.Length>4096 || body.Length>16384)return Task.FromResult(false);
            return owner.notify(identity.Id,identity.DisplayName,title,body);
        }
    }
    public string Enable(string file)
    {
        View.Dispatcher.VerifyAccess();PluginModule? module=null;
        try
        {
            module=new(file);var plugin=module.Plugin!;var identity=plugin.Identity;
            if(active.ContainsKey(identity.Id))throw new InvalidOperationException("同一插件已经启用。");
            var view=plugin.CreateWidget(new Host(this,identity)) ?? throw new InvalidOperationException("插件未返回组件。");
            if(view.Parent!=null)throw new InvalidOperationException("插件组件已有父级。");
            View.Children.Add(view);active[identity.Id]=new(module,view,identity);order.Add(identity.Id);Errors.Remove(identity.Id);return identity.Id;
        }
        catch{module?.Dispose();throw;}
    }
    public void Update(PluginFrame frame)
    {
        View.Dispatcher.VerifyAccess();
        foreach(var entry in order.Select(id=>new KeyValuePair<string,Active>(id,active[id])).ToArray())
        {
            try{entry.Value.Module.Plugin!.Update(frame);}
            catch(Exception error){Errors[entry.Key]="插件更新失败："+error.GetType().Name;Disable(entry.Key);}
        }
    }
    public void Disable(string id)
    {
        View.Dispatcher.VerifyAccess();if(!active.Remove(id,out var entry))return;order.Remove(id);
        if(entry.View.Parent is Panel panel)panel.Children.Remove(entry.View);else if(entry.View.Parent is Decorator decorator)decorator.Child=null;
        try{entry.Module.Dispose();}catch(Exception error){Errors[id]="插件释放失败："+error.GetType().Name;}
    }
    public bool Move(string id,int direction)
    {
        View.Dispatcher.VerifyAccess();var index=order.IndexOf(id);if(index<0)return false;
        var target=Math.Clamp(index+Math.Sign(direction),0,order.Count-1);if(target==index)return false;
        order.RemoveAt(index);order.Insert(target,id);var view=active[id].View;
        if(ReferenceEquals(view.Parent,View)){View.Children.Remove(view);View.Children.Insert(Math.Min(target,View.Children.Count),view);}return true;
    }
    public void Dispose(){foreach(var id in active.Keys.ToArray())Disable(id);}
}
