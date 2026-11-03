using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Capsule.PluginApi;

namespace Capsule.CenterClock;

public sealed class CenterClock : ICapsulePlugin
{
    public PluginIdentity Identity=>new("capsule.centerclock","居中时钟","0.1.0");
    private TextBlock? clock;
    private string last="";
    private bool? dark;
    public FrameworkElement CreateWidget(ICapsulePluginHost host)
    {
        clock=new TextBlock{FontSize=16,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,TextAlignment=TextAlignment.Center};
        clock.ToolTip="居中时钟 · 可在显示设置关闭内置时钟，避免重复显示";
        return clock;
    }
    public void Update(PluginFrame frame)
    {
        if(clock==null)return;
        var text=frame.Now.ToString("HH:mm");
        if(text!=last){clock.Text=text;last=text;}
        if(dark!=frame.DarkTheme){dark=frame.DarkTheme;clock.Foreground=frame.DarkTheme ? Brushes.White : new SolidColorBrush(Color.FromRgb(29,29,31));}
    }
    public void Dispose(){clock=null;}
}
