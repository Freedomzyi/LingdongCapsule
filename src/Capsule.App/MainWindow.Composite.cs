using Capsule.Core;
using System.Windows;
using System.Windows.Controls;

namespace Capsule.App;

internal sealed record DisplayComponent(string Id,string Name);
public partial class MainWindow
{
    private readonly Grid compactLayout=new(){ClipToBounds=true};
    private readonly Dictionary<string,Border> moduleHosts=[];
    private string[] arrangedModules=[];
    private bool modulesVisible;
    private double compactNaturalWidth;
    internal IReadOnlyList<string> LiveModuleOrder=>compactLayout.Children.OfType<Border>().Where(host=>host.Visibility==Visibility.Visible).Select(host=>(string)host.Tag).ToArray();
    private IReadOnlyList<string> EffectiveModuleOrder()=>ComponentOrder.Effective(settings.ComponentOrder,plugins.ActiveIds,settings.HardwareFirst,settings.MediaFirst);
    internal DisplayComponent[] DisplayComponents()=>EffectiveModuleOrder().Where(id=>id is "clock" or "hardware" or "media" || plugins.ActiveIds.Contains(id[7..])).Select(id=>new DisplayComponent(id,id switch{"clock"=>"*居中时钟","hardware"=>"硬件占用","media"=>"媒体",_=>plugins.Identity(id[7..]).DisplayName})).ToArray();
    private void InitializeComposite()
    {
        var root=(Grid)Shell.Child;root.Children.Remove(Clock);root.Children.Remove(MediaPanel);Clock.Children.Remove(HardwarePanel);
        root.Children.Insert(0,compactLayout);Clock.ClipToBounds=true;
    }
    private void RenderComposite(bool showMedia,bool combined,double widthLimit)
    {
        modulesVisible=Clock.Visibility==Visibility.Visible || combined;
        var showClock=modulesVisible && (settings.ClockEnabled || settings.StandbyActive && settings.StandbyScene==1);
        Clock.Visibility=showClock ? Visibility.Visible : Visibility.Collapsed;Clock.Margin=new Thickness(0);Clock.MaxWidth=double.PositiveInfinity;
        HardwarePanel.Visibility=modulesVisible && settings.HardwareEnabled && !settings.StandbyActive ? Visibility.Visible : Visibility.Collapsed;HardwarePanel.Margin=new Thickness(0);
        MediaPanel.Visibility=showMedia ? Visibility.Visible : Visibility.Collapsed;
        var views=new Dictionary<string,FrameworkElement>{{"clock",Clock},{"hardware",HardwarePanel},{"media",MediaPanel}};
        foreach(var id in plugins.ActiveIds){var widget=plugins.Widget(id);widget.Visibility=modulesVisible && !settings.StandbyActive ? Visibility.Visible : Visibility.Collapsed;views["plugin:"+id]=widget;}
        foreach(var id in moduleHosts.Keys.Where(id=>!views.ContainsKey(id)).ToArray()){var host=moduleHosts[id];host.Child=null;compactLayout.Children.Remove(host);moduleHosts.Remove(id);}
        var order=EffectiveModuleOrder().Where(views.ContainsKey).ToArray();
        foreach(var id in order)
        {
            if(!moduleHosts.TryGetValue(id,out var host)){host=new Border{ClipToBounds=true,Tag=id};host.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"IslandInk");moduleHosts[id]=host;}
            if(!ReferenceEquals(host.Child,views[id])){if(views[id].Parent is Panel parent)parent.Children.Remove(views[id]);host.Child=views[id];}
            host.Visibility=views[id].Visibility;
        }
        if(!order.SequenceEqual(arrangedModules))
        {
            compactLayout.Children.Clear();compactLayout.ColumnDefinitions.Clear();
            for(var index=0;index<order.Length;index++){var host=moduleHosts[order[index]];Grid.SetColumn(host,index);compactLayout.ColumnDefinitions.Add(new());compactLayout.Children.Add(host);}arrangedModules=order;
        }
        var visible=order.Where(id=>moduleHosts[id].Visibility==Visibility.Visible).ToArray();var natural=new Dictionary<string,double>();
        foreach(var id in visible){views[id].Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));natural[id]=Math.Clamp(views[id].DesiredSize.Width,0,widthLimit);}
        var available=Math.Max(0,widthLimit-26-compactLayout.Margin.Right);var reserve=showMedia ? Math.Min(110,available) : 0;
        var gap=visible.Length>1 ? Math.Min(8,Math.Max(0,(available-reserve)/(visible.Length-1))) : 0;
        var others=visible.Where(id=>id!="media").Sum(id=>natural[id]);var budget=Math.Max(0,available-reserve-gap*Math.Max(0,visible.Length-1));var ratio=others>0 ? Math.Min(1,budget/others) : 1;
        compactNaturalWidth=visible.Sum(id=>natural[id])+gap*Math.Max(0,visible.Length-1);
        compactLayout.Width=double.NaN;compactLayout.MaxWidth=available;compactLayout.HorizontalAlignment=showMedia ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        var prior=false;
        for(var index=0;index<order.Length;index++)
        {
            var id=order[index];var host=moduleHosts[id];var shown=host.Visibility==Visibility.Visible;var margin=shown && prior ? gap : 0;host.Margin=new Thickness(margin,0,0,0);
            compactLayout.ColumnDefinitions[index].Width=!shown ? new GridLength(0) : !showMedia ? GridLength.Auto : id=="media" ? new GridLength(1,GridUnitType.Star) : new GridLength(natural[id]*ratio+margin);
            if(shown)prior=true;
        }
    }
    internal void MoveDisplayComponent(string id,int direction)
    {
        var full=EffectiveModuleOrder().ToList();var visible=DisplayComponents().Select(item=>item.Id).ToList();var index=visible.IndexOf(id);if(index<0)return;
        var target=Math.Clamp(index+Math.Sign(direction),0,visible.Count-1);if(index==target)return;(visible[index],visible[target])=(visible[target],visible[index]);
        var next=0;var visibleSet=visible.ToHashSet();for(var slot=0;slot<full.Count;slot++)if(visibleSet.Contains(full[slot]))full[slot]=visible[next++];
        CommitDisplayOrder(full);
    }
    private void CommitDisplayOrder(List<string> order)
    {
        var old=settings.ComponentOrder;var files=settings.PluginFiles;var runtime=plugins.ActiveIds.ToArray();settings.ComponentOrder=order;
        SetRuntimePluginOrder(order.Where(id=>id.StartsWith("plugin:") && plugins.ActiveIds.Contains(id[7..])).Select(id=>id[7..]));settings.PluginFiles=plugins.ActiveIds.Select(id=>pluginPaths[id]).ToList();
        try{writePluginSettings();}catch{settings.ComponentOrder=old;settings.PluginFiles=files;SetRuntimePluginOrder(runtime);Render();throw;}Render();
    }
    private void SetRuntimePluginOrder(IEnumerable<string> ids)
    {
        var position=0;foreach(var id in ids.Where(plugins.ActiveIds.Contains)){while(plugins.ActiveIds.ToList().IndexOf(id)>position)plugins.Move(id,-1);position++;}
    }
}
