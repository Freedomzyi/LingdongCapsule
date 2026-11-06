using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Capsule.PluginApi;
using Capsule.Widgets;

namespace Capsule.FocusTimer;

public sealed class FocusTimer : ICapsulePlugin
{
    public PluginIdentity Identity=>new("capsule.focustimer","专注计时","0.1.0");
    private RingWidget? widget;
    private ICapsulePluginHost? host;
    private int minutes=25;
    private TimeSpan remaining=TimeSpan.FromMinutes(25);
    private DateTimeOffset deadline;
    private bool running,completed;
    private string last="";
    private string preferenceError="";
    public FrameworkElement CreateWidget(ICapsulePluginHost host)
    {
        this.host=host;
        if(int.TryParse(host.ReadSetting("minutes","25"),out var saved) && saved is 5 or 25 or 45)minutes=saved;
        remaining=TimeSpan.FromMinutes(minutes);
        widget=new(){Accent=Color.FromRgb(95,210,165),Cursor=Cursors.Hand};
        widget.MouseLeftButtonUp+=OnClick;widget.KeyDown+=OnKey;widget.MouseRightButtonUp+=OnMenu;
        var menu=new ContextMenu();
        foreach(var duration in new[]{25,45,5})
        {
            var item=new MenuItem{Header=duration==5 ? "休息 5 分钟" : $"专注 {duration} 分钟"};
            item.Click+=(_,_)=>
            {
                if(this.host==null || widget==null)return;
                try{this.host.WriteSetting("minutes",duration.ToString(System.Globalization.CultureInfo.InvariantCulture));}
                catch(Exception){preferenceError="时长未能保存，请检查设置文件后重试。";Paint();return;}
                preferenceError="";minutes=duration;Reset();
            };
            menu.Items.Add(item);
        }
        var reset=new MenuItem{Header="重置计时"};reset.Click+=(_,_)=>Reset();menu.Items.Add(new Separator());menu.Items.Add(reset);widget.ContextMenu=menu;
        Paint();return widget;
    }
    private void OnClick(object sender,MouseButtonEventArgs args){args.Handled=true;widget?.Focus();Toggle();}
    private void OnMenu(object sender,MouseButtonEventArgs args){args.Handled=true;if(widget?.ContextMenu is {} menu){menu.PlacementTarget=widget;menu.IsOpen=true;}}
    private void OnKey(object sender,KeyEventArgs args){if(args.Key is Key.Space or Key.Enter){args.Handled=true;Toggle();}}
    private void Toggle()
    {
        if(widget==null)return;
        if(running){remaining=Remaining(DateTimeOffset.UtcNow);running=false;}
        else{if(completed || remaining<=TimeSpan.Zero){remaining=TimeSpan.FromMinutes(minutes);completed=false;}deadline=DateTimeOffset.UtcNow+remaining;running=true;}
        Paint();
    }
    private TimeSpan Remaining(DateTimeOffset now)=>deadline>now ? deadline-now : TimeSpan.Zero;
    private void Reset(){if(widget==null)return;running=false;completed=false;remaining=TimeSpan.FromMinutes(minutes);Paint();}
    public void Update(PluginFrame frame)
    {
        if(widget==null)return;
        widget.Dark=frame.DarkTheme;
        if(running)
        {
            remaining=Remaining(frame.Now);
            if(remaining<=TimeSpan.Zero){running=false;completed=true;_=NotifyDoneAsync();}
        }
        Paint();
    }
    private async Task NotifyDoneAsync(){try{if(host!=null)await host.NotifyAsync("计时完成",minutes==5 ? "休息结束，准备好再继续。" : "这一段专注完成了，休息一下吧。");}catch(Exception){ /* A stopped host must not break the timer. */ }}
    private void Paint()
    {
        if(widget==null)return;
        widget.Caption=completed ? "已完成" : running ? (minutes==5 ? "休息中" : "专注中") : remaining.TotalMinutes<minutes ? "已暂停" : "专注计时";
        var seconds=Math.Max(0,(int)Math.Ceiling(remaining.TotalSeconds));widget.Value=$"{seconds/60:00}:{seconds%60:00}";
        widget.Progress=1-Math.Clamp(remaining.TotalSeconds/(minutes*60),0,1);
        widget.ToolTip=preferenceError.Length>0 ? preferenceError : "单击开始或暂停 · 右键设置时长与重置\n关闭或停用组件会结束本次计时。";
        System.Windows.Automation.AutomationProperties.SetName(widget,$"{widget.Caption} {widget.Value}");
        var key=$"{widget.Caption}|{widget.Value}|{widget.Dark}";if(last==key)return;last=key;widget.InvalidateVisual();
    }
    public void Dispose(){if(widget!=null){widget.MouseLeftButtonUp-=OnClick;widget.KeyDown-=OnKey;widget.MouseRightButtonUp-=OnMenu;if(widget.ContextMenu!=null)widget.ContextMenu.IsOpen=false;widget.ContextMenu=null;}widget=null;host=null;running=false;}
}
