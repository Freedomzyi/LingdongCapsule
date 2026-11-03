using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;

namespace Capsule.App;

internal sealed class WelcomeWindow : Window
{
    private WelcomeOptions choices;
    private bool startup,finishing;
    private int step;
    private readonly Action<WelcomeOptions?,bool?> complete;
    private readonly Action openSettings;
    private readonly ContentControl page=new();
    private readonly TextBlock progress=new(),error=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.IndianRed,FontSize=12};
    private readonly Button back=new(){Content="上一步",Padding=new(16,10,16,10)},next=new(){Padding=new(20,10,20,10),Foreground=Brushes.White,Background=new SolidColorBrush(Color.FromRgb(0,122,255)),IsDefault=true};

    internal WelcomeWindow(WelcomeOptions initial,bool startupEnabled,Action<WelcomeOptions?,bool?> complete,Action openSettings)
    {
        choices=initial;startup=startupEnabled;this.complete=complete;this.openSettings=openSettings;
        Title="欢迎使用灵动胶囊";Width=Math.Min(620,SystemParameters.WorkArea.Width-40);Height=Math.Min(510,SystemParameters.WorkArea.Height-48);
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        FontSize=13;Design.Bind(this,FontFamilyProperty,"UserFont");Design.Bind(this,ForegroundProperty,"TextPrimary");
        var outer=new Border{CornerRadius=new(24),Padding=new(26),BorderThickness=new(1),BorderBrush=new SolidColorBrush(Color.FromArgb(35,128,128,128))};Design.Bind(outer,Border.BackgroundProperty,"PageFill");Content=outer;
        var layout=new Grid();layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new());layout.RowDefinitions.Add(new(){Height=GridLength.Auto});outer.Child=layout;
        var header=new Grid{Margin=new(0,0,0,16)};header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var heading=new StackPanel();heading.Children.Add(new TextBlock{Text="灵动胶囊",FontSize=23,FontWeight=FontWeights.SemiBold});Design.Bind(progress,TextBlock.ForegroundProperty,"TextSecondary");progress.Margin=new(0,5,0,0);heading.Children.Add(progress);header.Children.Add(heading);
        var close=new Button{Content="×",Width=30,Height=30,FontSize=20,ToolTip="关闭引导，下次启动可继续"};close.Click+=(_,_)=>Close();Grid.SetColumn(close,1);header.Children.Add(close);
        header.MouseLeftButtonDown+=(_,e)=>{if(ReferenceEquals(e.OriginalSource,header) && e.LeftButton==MouseButtonState.Pressed)DragMove();};layout.Children.Add(header);
        Grid.SetRow(page,1);layout.Children.Add(page);
        var footer=new StackPanel{Margin=new(0,16,0,0)};footer.Children.Add(error);
        var actions=new Grid();actions.ColumnDefinitions.Add(new());actions.ColumnDefinitions.Add(new(){Width=GridLength.Auto});actions.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var skip=new Button{Content="稍后设置",HorizontalAlignment=HorizontalAlignment.Left,Padding=new(12,10,12,10),ToolTip="保留现有选择并结束本次引导"};skip.Click+=(_,_)=>Finish(false);actions.Children.Add(skip);
        back.Margin=new(0,0,8,0);back.Click+=(_,_)=>{step--;RenderPage();};Grid.SetColumn(back,1);actions.Children.Add(back);
        next.Click+=(_,_)=>{if(step<3){step++;RenderPage();}else Finish(true);};Grid.SetColumn(next,2);actions.Children.Add(next);footer.Children.Add(actions);Grid.SetRow(footer,2);layout.Children.Add(footer);
        RenderPage();Loaded+=(_,_)=>BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)));
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;Close();}};
    }
    private bool Finish(bool apply)
    {
        if(finishing)return false;finishing=true;error.Text="";
        try{complete(apply ? choices : null,apply ? startup : null);}
        catch(Exception exception){error.Text="未能保存，请重试："+exception.Message;finishing=false;return false;}
        Close();return true;
    }
    private void RenderPage()
    {
        error.Text="";progress.Text=$"{step+1} / 4 · {new[]{"初次见面","选择外观","放在合适的位置","准备开始"}[step]}";
        back.Visibility=step==0 ? Visibility.Collapsed : Visibility.Visible;next.Content=step==3 ? "开始使用" : "下一步";
        var panel=new StackPanel();var scroll=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};page.Content=scroll;
        if(step==0)
        {
            panel.Children.Add(new Image{Source=new BitmapImage(new Uri("pack://application:,,,/LingdongCapsule;component/Assets/icon.png")),Width=68,Height=68,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,2,0,16)});
            Heading(panel,"时间、音乐与消息，收在屏幕上沿。");
            Note(panel,"点击胶囊展开音乐控制；没有媒体时，可以查看天气与设备状态。右键胶囊或双击托盘图标打开设置。");
            Note(panel,"接下来选择外观和显示位置，点击“开始使用”时统一保存。以后仍可在设置中调整。");
        }
        else if(step==1)
        {
            Heading(panel,"选择你的外观");
            Options(panel,"岛体形态",new[]{"刘海","灵动胶囊"},choices.Shape,value=>choices=choices with{Shape=value});
            Options(panel,"界面主题",new[]{"深色","浅色","跟随系统"},choices.Theme,value=>choices=choices with{Theme=value});
            Options(panel,"背景材质",new[]{"实体","亚克力","液态玻璃"},choices.Material,value=>choices=choices with{Material=value});
            Note(panel,"亚克力与玻璃在内存中合成桌面背景，不录制或上传；不支持的环境会回退实体背景。");
        }
        else if(step==2)
        {
            Heading(panel,"选择显示位置与模式");
            var monitors=MonitorPlacement.Options().ToDictionary(item=>item.Key,item=>item.Value);
            if(!monitors.ContainsKey(choices.Monitor))monitors[choices.Monitor]="已断开的显示器 · 临时使用主屏";
            Label(panel,"目标显示器");var selector=new Button{Tag="welcome-monitor",Content=monitors[choices.Monitor]+"  ⌄",Padding=new(12,10,12,10),HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new(0,0,0,16)};
            selector.Click+=(_,_)=>
            {
                var menu=new ContextMenu{PlacementTarget=selector,Placement=PlacementMode.Bottom};selector.ContextMenu=menu;
                foreach(var monitor in monitors)
                {
                    var item=new MenuItem{Header=(monitor.Key==choices.Monitor ? "✓  " : "")+monitor.Value,Tag=monitor.Key};
                    item.Click+=(_,_)=>{choices=choices with{Monitor=monitor.Key};selector.Content=monitor.Value+"  ⌄";menu.IsOpen=false;};menu.Items.Add(item);
                }
                menu.IsOpen=true;
            };panel.Children.Add(selector);
            Options(panel,"显示模式",new[]{"普通模式","待机模式"},choices.Standby ? 1 : 0,value=>choices=choices with{Standby=value==1});
            Note(panel,"普通模式按你的组件设置显示；待机模式使用已选待机场景。断开显示器时暂回主屏，保留原有选择。岛体默认高度为 35 逻辑像素，可在显示设置调节。");
        }
        else
        {
            Heading(panel,"准备好开始使用");
            var startupSwitch=new CheckBox{Tag="welcome-startup",Content="登录 Windows 后自动启动",IsChecked=startup,Margin=new(0,10,0,18)};startupSwitch.Click+=(_,_)=>startup=startupSwitch.IsChecked==true;panel.Children.Add(startupSwitch);
            Note(panel,"程序日常仅在托盘常驻，不占任务栏。手动再次运行可以唤回设置；托盘右键可隐藏、唤出或退出。");
            Note(panel,"系统通知需要在设置中授权；声音、头像读取和外部接入分别由你控制。仅有提示声而未生成系统通知的消息不会自动出现。");
            var settingsButton=new Button{Content="完成并打开设置",HorizontalAlignment=HorizontalAlignment.Left,Padding=new(14,9,14,9),Margin=new(0,10,0,0)};
            settingsButton.Click+=(_,_)=>{if(Finish(true))openSettings();};panel.Children.Add(settingsButton);
        }
        scroll.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)));
    }
    private static void Heading(StackPanel panel,string text)=>panel.Children.Add(new TextBlock{Text=text,FontSize=19,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,14)});
    private static void Label(StackPanel panel,string text)=>panel.Children.Add(new TextBlock{Text=text,FontWeight=FontWeights.Medium,Margin=new(0,0,0,8)});
    private static void Note(StackPanel panel,string text)
    {var note=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,LineHeight=20,Margin=new(0,0,0,12)};Design.Bind(note,TextBlock.ForegroundProperty,"TextSecondary");panel.Children.Add(note);}
    private static void Options(StackPanel panel,string label,string[] values,int selected,Action<int> change)
    {
        Label(panel,label);var row=new UniformGrid{Columns=values.Length,Margin=new(0,0,0,16)};var buttons=new List<Button>();
        for(var index=0;index<values.Length;index++)
        {
            var value=index;var button=new Button{Content=values[index],Tag="welcome:"+label+":"+value,Padding=new(8,9,8,9),Margin=new(0,0,index==values.Length-1 ? 0 : 6,0)};buttons.Add(button);row.Children.Add(button);
            button.Click+=(_,_)=>{selected=value;change(value);Paint();};
        }
        void Paint(){for(var index=0;index<buttons.Count;index++){if(index==selected){buttons[index].Background=new SolidColorBrush(Color.FromRgb(0,122,255));buttons[index].Foreground=Brushes.White;}else{Design.Bind(buttons[index],Control.BackgroundProperty,"ButtonFill");Design.Bind(buttons[index],Control.ForegroundProperty,"TextPrimary");}}}
        Paint();panel.Children.Add(row);
    }
}
