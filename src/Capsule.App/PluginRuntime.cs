using Capsule.PluginApi;
using System.Windows;
using System.Windows.Controls;

namespace Capsule.App;

internal sealed class PluginRuntime : IDisposable
{
    private sealed record Active(PluginModule Module,FrameworkElement View,PluginIdentity Identity,Host Host);
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
    internal PluginTimelineReading? ReadTimeline(PluginMediaSession session)
    {
        View.Dispatcher.VerifyAccess();PluginTimelineReading? best=null;
        foreach(var id in order.ToArray())
        {
            if(!active.TryGetValue(id,out var entry) || entry.Module.Plugin is not ICapsuleTimelineSource source)continue;
            try
            {
                var value=source.ReadTimeline(session);
                if(value!=null && PluginTimelineClock.Valid(value) && (best==null || value.Priority>best.Timeline.Priority))best=new(id,value);
            }
            catch(Exception error){Errors[id]="插件时间轴读取失败："+error.GetType().Name;Disable(id);}
        }
        return best;
    }
    public Dictionary<string,string> Errors{get;}=[];
    public PluginRuntime(Settings settings,Func<PluginMedia> media,Func<string,string,string,string,Task<bool>> notify,Action save){this.settings=settings;this.media=media;this.notify=notify;this.save=save;}
    private sealed class Host(PluginRuntime owner,PluginIdentity identity) : ICapsulePluginHost
    {
        private int closed;
        internal void Close()=>Volatile.Write(ref closed,1);
        private void Check(){ObjectDisposedException.ThrowIf(Volatile.Read(ref closed)!=0,this);owner.View.Dispatcher.VerifyAccess();}
        public PluginMedia CurrentMedia{get{Check();return owner.media();}}
        public string ReadSetting(string key,string fallback=""){Check();return owner.settings.PluginPreferences.TryGetValue(identity.Id,out var values) ? values.GetValueOrDefault(key,fallback) : fallback;}
        public void WriteSetting(string key,string value)
        {
            Check();
            if(string.IsNullOrWhiteSpace(key) || key.Length>80 || value==null || value.Length>8192)throw new ArgumentException("Invalid plugin setting");
            var created=!owner.settings.PluginPreferences.TryGetValue(identity.Id,out var values);values??=[];
            if(values.Count>=128 && !values.ContainsKey(key))throw new InvalidOperationException("Plugin setting limit reached");
            var existed=values.TryGetValue(key,out var previous);values[key]=value;if(created)owner.settings.PluginPreferences[identity.Id]=values;
            try{owner.save();}
            catch{if(created)owner.settings.PluginPreferences.Remove(identity.Id);else if(existed)values[key]=previous!;else values.Remove(key);throw;}
        }
        public Task<bool> NotifyAsync(string title,string body)
        {
            if(Volatile.Read(ref closed)!=0 || owner.View.Dispatcher.HasShutdownStarted || title==null || body==null || title.Length>4096 || body.Length>16384)return Task.FromResult(false);
            Task<bool> Send()=>Volatile.Read(ref closed)!=0 ? Task.FromResult(false) : owner.notify(identity.Id,identity.DisplayName,title,body);
            return owner.View.Dispatcher.CheckAccess() ? Send() : owner.View.Dispatcher.InvokeAsync(Send).Task.Unwrap();
        }
    }
    public string Enable(string file)
    {
        View.Dispatcher.VerifyAccess();PluginModule? module=null;Host? host=null;
        try
        {
            module=new(file);var plugin=module.Plugin!;var identity=plugin.Identity;
            if(active.ContainsKey(identity.Id))throw new InvalidOperationException("同一插件已经启用。");
            host=new(this,identity);var view=plugin.CreateWidget(host) ?? throw new InvalidOperationException("插件未返回组件。");
            if(view.Parent!=null)throw new InvalidOperationException("插件组件已有父级。");
            View.Children.Add(view);active[identity.Id]=new(module,view,identity,host);order.Add(identity.Id);Errors.Remove(identity.Id);return identity.Id;
        }
        catch{try{module?.Dispose();}catch(Exception error){Errors[file]="加载失败后释放异常："+error.GetType().Name;}finally{host?.Close();}throw;}
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
        finally{entry.Host.Close();}
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
