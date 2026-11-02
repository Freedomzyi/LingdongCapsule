using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Capsule.App;
public sealed class SettingsWindow : Window
{
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new(0,12,0,12) };
    private readonly StackPanel sources = new();
    private readonly Settings settings;
    private readonly Action save;
    private string sourceKey = "";
    public SettingsWindow(Settings settings, Func<Task<string>> request, Action save, Action test)
    {
        this.settings = settings; this.save = save;
        Title = "灵动胶囊 · 设置"; Width = 420; Height = 590; MinWidth = 360; MinHeight = 420;
        Background = new SolidColorBrush(Color.FromRgb(247,247,250)); FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
        var panel = new StackPanel { Margin = new(24) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "灵动胶囊", FontSize = 24, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "0.1.0 · Windows 桌面交互胶囊", Foreground = Brushes.Gray, Margin = new(0,4,0,18) });
        AddSlider(panel, "播放与通知宽度", 220,480, settings.Width, v => settings.Width=v, "逻辑像素；控制播放宽度与通知宽度上限");
        AddSlider(panel, "收放时长",150,900,settings.MotionMs,v=>settings.MotionMs=(int)v,"毫秒；越大越舒缓");
        AddCheck(panel,"截图中显示胶囊",settings.ShowInCapture,v=>settings.ShowInCapture=v,"关闭后请求 Windows 从兼容的截图和录屏中排除主岛体，设置窗口不在此范围");
        AddCheck(panel,"通知正文隐私预览",settings.PrivatePreview,v=>settings.PrivatePreview=v,"展开前仅显示应用名和新消息提示；不保存通知正文");
        AddCheck(panel,"监听系统通知",settings.NotificationsEnabled,v=>settings.NotificationsEnabled=v,"须先授权；只接收进入 Windows 通知中心的通知，不修改系统横幅策略");
        var authorize = new Button { Content = "授权系统通知", Padding = new(12,7,12,7), Margin = new(0,12,0,0) };
        authorize.Click += async (_,_) => { authorize.IsEnabled=false; try { status.Text=await request(); } finally { authorize.IsEnabled=true; } };
        panel.Children.Add(authorize);
        var testButton = new Button { Content="显示本地测试通知", Padding=new(12,7,12,7), Margin=new(0,8,0,0) };
        testButton.Click += (_,_) => test(); panel.Children.Add(testButton); panel.Children.Add(status);
        panel.Children.Add(new TextBlock { Text="通知来源", FontWeight=FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text="授权后列出通知中心中已发现的应用。取消勾选可静音该来源。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Gray,Margin=new(0,5,0,8) });
        panel.Children.Add(sources);
    }
    private void AddSlider(StackPanel panel,string title,double min,double max,double value,Action<double> changed,string help)
    {
        var label=new TextBlock { Text=$"{title}  {value:0}", Margin=new(0,8,0,4), ToolTip=help }; panel.Children.Add(label);
        var slider=new Slider {Minimum=min,Maximum=max,Value=value,TickFrequency=10,IsSnapToTickEnabled=true,ToolTip=help};
        slider.ValueChanged+=(_,_)=>{changed(slider.Value);label.Text=$"{title}  {slider.Value:0}";save();};panel.Children.Add(slider);
    }
    private void AddCheck(StackPanel panel,string text,bool value,Action<bool> changed,string help)
    {
        var check=new CheckBox {Content=text,IsChecked=value,Margin=new(0,13,0,0),ToolTip=help};
        check.Click+=(_,_)=>{changed(check.IsChecked==true);save();};panel.Children.Add(check);
    }
    public void UpdateStatus(string media,string notification,Dictionary<string,string> apps)
    {
        status.Text=media+"\n"+notification;
        var key=string.Join("|",apps.OrderBy(x=>x.Key).Select(x=>x.Key)); if(key==sourceKey)return;sourceKey=key;sources.Children.Clear();
        foreach(var app in apps.OrderBy(x=>x.Value))
        {
            var id=app.Key;var check=new CheckBox {Content=app.Value,IsChecked=!settings.MutedSources.Contains(id),Margin=new(0,5,0,5),ToolTip=id};
            check.Click+=(_,_)=>{if(check.IsChecked==true)settings.MutedSources.Remove(id);else settings.MutedSources.Add(id);save();};sources.Children.Add(check);
        }
    }
}
