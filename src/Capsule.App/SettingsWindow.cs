using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace Capsule.App;

public sealed class SettingsWindow : Window
{
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 19 };
    private readonly StackPanel sources = new();
    private readonly Settings settings;
    private readonly Action save;
    private string sourceKey = "__initial__";
    private CheckBox? notificationsCheck;
    private Button? authorizeButton;
    private readonly Dictionary<string,StackPanel> pages=[];
    private readonly Dictionary<string,ScrollViewer> pageViews=[];
    private readonly Dictionary<string,Button> tabs=[];
    private readonly ContentControl pageHost=new();

    public SettingsWindow(Settings settings, Func<Task<string>> request, Action save, Action test, Action<bool> startup, Action welcome,Func<IReadOnlyDictionary<string,string>> mediaSources)
    {
        this.settings = settings; this.save = save;
        Title = "灵动胶囊 · 设置"; Width = Math.Min(700,SystemParameters.WorkArea.Width-40); Height = Math.Min(720, SystemParameters.WorkArea.Height - 48);
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        FontFamily = new("Microsoft YaHei UI"); FontSize = 13; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Design.Bind(this, ForegroundProperty, "TextPrimary");
        var outer = new Border { CornerRadius = new(22), BorderBrush = new SolidColorBrush(Color.FromArgb(35, 128, 128, 128)), BorderThickness = new(1) };
        Design.Bind(outer, Border.BackgroundProperty, "PageFill"); Content = outer;
        var layout = new Grid(); layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new()); outer.Child = layout;
        var header = new Grid { Margin = new(24, 20, 16, 12) }; header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var brand = new StackPanel(); brand.Children.Add(new TextBlock { Text = "灵动胶囊", FontSize = 25, FontWeight = FontWeights.SemiBold });
        var subtitle = new TextBlock { Text = "让重要的信息，轻轻出现。", FontSize = 12, Margin = new(0, 5, 0, 0) }; Design.Bind(subtitle, TextBlock.ForegroundProperty, "TextSecondary"); brand.Children.Add(subtitle); header.Children.Add(brand);
        var close = new Button { Content = "×", Width = 30, Height = 30, FontSize = 20, VerticalAlignment = VerticalAlignment.Top, ToolTip = "关闭设置" }; close.Click += (_, _) => Close(); Grid.SetColumn(close, 1); header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not Button && e.LeftButton == MouseButtonState.Pressed) DragMove(); }; layout.Children.Add(header);
        var content=new Grid();content.ColumnDefinitions.Add(new(){Width=new GridLength(154)});content.ColumnDefinitions.Add(new());Grid.SetRow(content,1);layout.Children.Add(content);
        var navigation=new StackPanel {Margin=new Thickness(12,12,6,12)};content.Children.Add(navigation);
        Grid.SetColumn(pageHost,1);content.Children.Add(pageHost);
        foreach(var name in new[]{"个性化中心","通用设置","显示设置","媒体设置","交互设置","关于软件"})
        {
            var page=new StackPanel {Margin=new Thickness(16,0,24,20)};pages[name]=page;
            pageViews[name]=new ScrollViewer {Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            var button=new Button {Content=name,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,10,12,10),Margin=new Thickness(0,0,0,5),Background=Brushes.Transparent};
            button.Click+=(_,_)=>SelectPage(name);tabs[name]=button;navigation.Children.Add(button);
        }
        SelectPage("个性化中心");
        var panel=pages["个性化中心"];

        Heading(panel, "外观与动效");
        var appearance = new StackPanel();
        AddSlider(appearance, "播放与通知宽度", 220, 480, settings.Width, v => settings.Width = v, "逻辑像素；播放固定宽度，通知不会超过此值。");
        AddSlider(appearance, "折叠高度", 32, 60, settings.CompactHeight, v => settings.CompactHeight = v, "逻辑像素；默认 35，控制时钟与紧凑媒体高度，不压缩展开内容。", 1);
        AddSlider(appearance, "收放时长", 150, 900, settings.MotionMs, v => settings.MotionMs = (int)v, "毫秒；统一控制展开和收起，数值越大越舒缓。");
        panel.Children.Add(Design.Card(appearance));
        var displayBehavior=new StackPanel();
        AddCheck(displayBehavior,"媒体与时钟自动切换",settings.MediaClockSwitch,v=>settings.MediaClockSwitch=v,"播放时显示媒体，暂停后收回时钟；有会话时点击时钟仍可展开控制。");
        AddCheck(displayBehavior,"播放进度线",settings.CoverProgress,v=>settings.CoverProgress=v,"沿胶囊外沿显示当前歌曲的播放进度。");
        panel.Children.Add(Design.Card(displayBehavior));
        panel=pages["通用设置"];
        var switches = new StackPanel();
        AddCheck(switches,"窗口置顶",settings.Topmost,v=>settings.Topmost=v,"在普通应用窗口上方显示胶囊。");
        AddCheck(switches, "截图中显示胶囊", settings.ShowInCapture, v => settings.ShowInCapture = v, "关闭后请求 Windows 从兼容截图和录屏中排除主岛体；设置窗口不在此范围。");
        AddCheck(switches, "通知隐私预览", settings.PrivatePreview, v => settings.PrivatePreview = v, "展开前仅显示应用名与新消息提示，通知正文不写入磁盘。");
        notificationsCheck = AddCheck(switches, "系统通知", settings.NotificationsEnabled, v => settings.NotificationsEnabled = v, "只接收 Windows 通知中心可提供的通知，不修改系统横幅策略。");
        AddCheck(switches, "开机启动", StartupService.IsEnabled, startup, "登录当前 Windows 用户后自动启动，只驻留系统托盘；不需要管理员权限。");
        panel.Children.Add(Design.Card(switches));

        panel=pages["显示设置"];
        Heading(panel,"显示位置与形态");
        var positionOptions=new StackPanel();
        AddSelection(positionOptions,"目标显示器",MonitorPlacement.Options,settings.MonitorId,v=>settings.MonitorId=v,"显示器断开时回到主屏，不修改其他软件设置。");
        AddSelection(positionOptions,"岛体形态",()=>new Dictionary<string,string>{{"1","灵动胶囊"},{"0","刘海"}},settings.IslandShape.ToString(),v=>settings.IslandShape=int.Parse(v),"刘海贴合屏幕上沿；胶囊保留顶部间距。");
        AddSlider(positionOptions,"刘海底部圆角",0,28,settings.BottomRadius,v=>settings.BottomRadius=v,"逻辑像素，仅刘海形态生效。",1);
        panel.Children.Add(Design.Card(positionOptions));
        panel=pages["媒体设置"];
        Heading(panel,"媒体识别");
        var mediaOptions=new StackPanel();
        AddCheck(mediaOptions,"媒体控制",settings.MediaEnabled,v=>settings.MediaEnabled=v,"接入系统媒体会话；关闭后停止媒体与歌词接入。");
        AddSelection(mediaOptions,"播放器类型",()=>new Dictionary<string,string>{{"other","其他软件 / 自动"},{"browser","浏览器媒体"},{"netease","网易云音乐"},{"qqmusic","QQ 音乐"},{"kugou","酷狗音乐"},{"spotify","Spotify"},{"apple","Apple Music"},{"echo","Echo Music"},{"lxmusic","LX Music"}},settings.MediaSourceKind,v=>settings.MediaSourceKind=v,"自动匹配时按软件类型筛选；手动指定软件后优先使用指定会话。");
        AddSelection(mediaOptions,"指定媒体软件",()=>{var options=new Dictionary<string,string>{{"","自动匹配"}};foreach(var item in mediaSources())options[item.Key]=item.Value;return options;},settings.ManualMediaSource,v=>settings.ManualMediaSource=v,"手动选择已出现的系统媒体会话，将优先于播放器类型。");
        AddCheck(mediaOptions,"实时频谱",settings.ShowSpectrum,v=>settings.ShowSpectrum=v,"分析本机系统输出，显示五组频段；不录音、不上传声音。");
        panel.Children.Add(Design.Card(mediaOptions));
        Heading(panel,"歌词与译文");
        var lyricOptions=new StackPanel();
        AddSlider(lyricOptions,"歌词偏移",-3,3,settings.LyricOffsetSeconds,v=>settings.LyricOffsetSeconds=v,"秒；正值提前，负值延后。",0.1);
        AddCheck(lyricOptions,"显示歌词",settings.LyricsEnabled,v=>settings.LyricsEnabled=v,"根据当前歌曲信息联网匹配同步歌词；关闭后不联网取词，无匹配时显示标题。");
        AddCheck(lyricOptions,"显示译文",settings.TranslationEnabled,v=>settings.TranslationEnabled=v,"歌词源有译文时显示第二行。");
        AddCheck(lyricOptions,"逐字高亮",settings.KaraokeEnabled,v=>settings.KaraokeEnabled=v,"优先使用真实逐字时序，没有时使用整行进度渐亮。");
        AddCheck(lyricOptions,"长歌词横向滚动",settings.ScrollLyrics,v=>settings.ScrollLyrics=v,"短句居中，超出歌词区域的长句按进度平滑横移。");
        panel.Children.Add(Design.Card(lyricOptions));
        panel=pages["交互设置"];
        Heading(panel,"媒体交互");
        var interaction=new StackPanel();
        AddCheck(interaction,"点击展开媒体控制",settings.ExpandedMediaControls,v=>settings.ExpandedMediaControls=v,"开启时点击展开完整控制；关闭时在紧凑岛体中显示内联播放按钮。");
        AddCheck(interaction,"点击封面返回播放器",settings.ReturnToPlayer,v=>settings.ReturnToPlayer=v,"单击封面唤回当前媒体软件，歌词和时钟区域仍用于展开控制。");
        panel.Children.Add(Design.Card(interaction));
        panel=pages["通用设置"];
        Heading(panel, "连接与体验");
        var connection = new StackPanel();
        Design.Bind(status, TextBlock.ForegroundProperty, "TextSecondary"); connection.Children.Add(status);
        var authorize = new Button { Content = "授权系统通知", Padding = new(12, 10, 12, 10), Margin = new(0, 12, 0, 8), Background = new SolidColorBrush(Color.FromRgb(0, 122, 255)), Foreground = Brushes.White };
        authorizeButton = authorize;
        authorize.Click += async (_, _) => { authorize.IsEnabled = false; try { status.Text = await request(); } finally { authorize.IsEnabled = true; } };
        connection.Children.Add(authorize);
        var testButton = new Button { Content = "试一条通知", Padding = new(12, 10, 12, 10) }; testButton.Click += (_, _) => test(); connection.Children.Add(testButton);
        var guide = new Button { Content = "使用指南", Padding = new(12, 10, 12, 10), Margin = new(0, 8, 0, 0) }; guide.Click += (_, _) => welcome(); connection.Children.Add(guide);
        panel.Children.Add(Design.Card(connection));

        Heading(panel, "通知来源");
        var help = new TextBlock { Text = "已发现的应用显示在这里。关闭开关即可静音该来源。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(0, 0, 0, 10) }; Design.Bind(help, TextBlock.ForegroundProperty, "TextSecondary"); panel.Children.Add(help);
        panel.Children.Add(Design.Card(sources));
        panel=pages["关于软件"];
        Heading(panel,"灵动胶囊");
        var about=new StackPanel();
        var description=new TextBlock {Text="独立 Windows 桌面交互胶囊。\n\n正在完整对齐本机 NotchPeninsula 定制版的功能与交互；当前为开发版本。",TextWrapping=TextWrapping.Wrap,LineHeight=22,Margin=new Thickness(0,8,0,14)};about.Children.Add(description);
        var repository=new Button {Content="GitHub 源码与进度",Padding=new Thickness(12,10,12,10)};repository.Click+=(_,_)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/Freedomzyi/LingdongCapsule"){UseShellExecute=true});about.Children.Add(repository);
        panel.Children.Add(Design.Card(about));
        var footer = new TextBlock { Text = "0.1.0 开发版  ·  MIT License", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 6, 0, 0) }; Design.Bind(footer, TextBlock.ForegroundProperty, "TextSecondary"); panel.Children.Add(footer);
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }
    public void SelectPage(string name)
    {
        if(!pageViews.TryGetValue(name,out var view))return;
        pageHost.Content=view;
        foreach(var tab in tabs){tab.Value.Background=tab.Key==name ? (Brush)System.Windows.Application.Current.FindResource("ButtonFill") : Brushes.Transparent;tab.Value.Foreground=tab.Key==name ? new SolidColorBrush(Color.FromRgb(0,122,255)) : (Brush)System.Windows.Application.Current.FindResource("TextPrimary");}
        pageHost.BeginAnimation(OpacityProperty,new DoubleAnimation(0.65,1,TimeSpan.FromMilliseconds(150)));
    }
    private void AddSelection(StackPanel parent,string title,Func<IReadOnlyDictionary<string,string>> readOptions,string selected,Action<string> changed,string help)
    {
        var grid=new Grid {Margin=new Thickness(0,8,0,8)};grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});grid.Children.Add(Label(title,help));
        var options=readOptions();
        var text=new TextBlock {Text=(options.TryGetValue(selected,out var label) ? label : selected)+" ▾",MaxWidth=176,TextTrimming=TextTrimming.CharacterEllipsis};
        var button=new Button {Content=text,Padding=new Thickness(10,7,10,7),MaxWidth=200};System.Windows.Automation.AutomationProperties.SetName(button,title);
        button.Click+=(_,_)=>
        {
            var menu=new ContextMenu {PlacementTarget=button,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};
            foreach(var option in readOptions())
            {
                var key=option.Key;var value=option.Value;
                var item=new MenuItem {Header=value+(key==selected ? "  ✓" : "")};
                item.Click+=(_,_)=>{selected=key;changed(key);text.Text=value+" ▾";save();};menu.Items.Add(item);
            }
            menu.IsOpen=true;
        };
        Grid.SetColumn(button,1);grid.Children.Add(button);parent.Children.Add(grid);
    }
    private static void Heading(StackPanel panel, string text)
    {
        var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.Medium, Margin = new(4, 12, 0, 8) }; Design.Bind(label, TextBlock.ForegroundProperty, "TextSecondary"); panel.Children.Add(label);
    }
    private static StackPanel Label(string text, string help)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var info = new TextBlock { Text = "ⓘ", FontSize = 12, Margin = new(6, 0, 0, 0), ToolTip = help, VerticalAlignment = VerticalAlignment.Center }; Design.Bind(info, TextBlock.ForegroundProperty, "TextSecondary"); row.Children.Add(info); return row;
    }
    private void AddSlider(StackPanel panel, string title, double min, double max, double value, Action<double> changed, string help, double step = 10)
    {
        var row = new Grid { Margin = new(0, 9, 0, 0) }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.Children.Add(Label(title, help));
        var format=step<1 ? "0.0" : "0";
        var number = new TextBlock { Text = value.ToString(format), FontSize = 13, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 255)) }; Grid.SetColumn(number, 1); row.Children.Add(number); panel.Children.Add(row);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = step, IsSnapToTickEnabled = true, Margin = new(0, 4, 0, 7), ToolTip = help };
        slider.ValueChanged += (_, _) => { changed(slider.Value); number.Text = slider.Value.ToString(format); save(); }; panel.Children.Add(slider);
    }
    private CheckBox AddCheck(StackPanel panel, string text, bool value, Action<bool> changed, string help)
    {
        var check = new CheckBox { Content = Label(text, help), IsChecked = value, Margin = new(0, 4, 0, 4), ToolTip = help }; System.Windows.Automation.AutomationProperties.SetName(check, text);
        check.Click += (_, _) => { changed(check.IsChecked == true); save(); }; panel.Children.Add(check); return check;
    }
    public void UpdateStatus(string media, string notification, Dictionary<string, string> apps)
    {
        status.Text = media + "\n" + notification; if (notificationsCheck is not null) notificationsCheck.IsChecked = settings.NotificationsEnabled;
        if (authorizeButton is not null) authorizeButton.Visibility = notification.Contains("正常") || notification.Contains("已授权") ? Visibility.Collapsed : Visibility.Visible;
        var key = string.Join("|", apps.OrderBy(x => x.Key).Select(x => x.Key + ":" + x.Value)); if (key == sourceKey) return; sourceKey = key; sources.Children.Clear();
        foreach (var app in apps.OrderBy(x => x.Value))
        {
            var id = app.Key; var label = new TextBlock { Text = app.Value, MaxWidth = 238, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var check = new CheckBox { Content = label, IsChecked = !settings.MutedSources.Contains(id), Margin = new(0, 4, 0, 4), ToolTip = app.Value }; System.Windows.Automation.AutomationProperties.SetName(check, app.Value);
            check.Click += (_, _) => { if (check.IsChecked == true) settings.MutedSources.Remove(id); else settings.MutedSources.Add(id); save(); }; sources.Children.Add(check);
        }
        if (apps.Count == 0) { var empty = new TextBlock { Text = "收到系统通知后，来源会自动出现在这里。", TextWrapping = TextWrapping.Wrap, FontSize = 12 }; Design.Bind(empty, TextBlock.ForegroundProperty, "TextSecondary"); sources.Children.Add(empty); }
    }
}
