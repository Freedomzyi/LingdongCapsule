using System.Windows;
using System.Windows.Media;
using Capsule.PluginApi;
using Capsule.Widgets;

namespace Capsule.DayProgress;

public sealed class DayProgress : ICapsulePlugin
{
    public PluginIdentity Identity=>new("capsule.dayprogress","今日进度","0.1.0");
    private RingWidget? widget;
    private string last="";
    public FrameworkElement CreateWidget(ICapsulePluginHost host)=>widget=new(){Caption="今日进度",Value="—",Accent=Color.FromRgb(100,166,255),Focusable=false};
    public void Update(PluginFrame frame)
    {
        if(widget==null)return;
        // Wall-clock progress of the current local calendar day, not a productivity score.
        var progress=frame.Now.TimeOfDay.TotalSeconds/86400;
        var value=$"{progress*100:0}%";
        var key=$"{frame.Now:yyyyMMddHHmm}|{frame.DarkTheme}";if(key==last)return;last=key;
        widget.Progress=progress;widget.Value=value;widget.Dark=frame.DarkTheme;
        System.Windows.Automation.AutomationProperties.SetName(widget,$"今日进度 {value}");
        widget.ToolTip=$"{frame.Now:MM月dd日 dddd} · 今日已过 {progress*100:0.0}%";
        widget.InvalidateVisual();
    }
    public void Dispose(){widget=null;}
}
