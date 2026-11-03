using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Capsule.Core;
namespace Capsule.App;

public sealed class SettingsWindow : Window
{
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 19 };
    private readonly StackPanel sources = new();
    private readonly Settings settings;
    private readonly Action save;
    private string sourceKey = "__initial__";
    private CheckBox? notificationsCheck;
    private CheckBox? quietCheck;
    private CheckBox? mspCheck;
    private Button? authorizeButton;
    private readonly Dictionary<string,StackPanel> pages=[];
    private readonly Dictionary<string,ScrollViewer> pageViews=[];
    private readonly Dictionary<string,Button> tabs=[];
    private readonly ContentControl pageHost=new();
    private readonly NotificationSound soundPreview=new();

    internal SettingsWindow(Settings settings, Func<Task<string>> request, Action save, Action test, Action<bool> startup, Action welcome,Func<IReadOnlyDictionary<string,string>> mediaSources,GlobalShortcuts shortcuts,Action<string> loadPlugin,Func<string[]> activePlugins,Action<string> disablePlugin,Action<string,int> movePlugin,Action<string> uninstallPlugin)
    {
        this.settings = settings; this.save = save;
        Closed+=(_,_)=>soundPreview.Dispose();
        Title = "灵动胶囊 · 设置"; Width = Math.Min(700,SystemParameters.WorkArea.Width-40); Height = Math.Min(720, SystemParameters.WorkArea.Height - 48);
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        FontFamily = Design.UserFont; FontSize = 13; WindowStartupLocation = WindowStartupLocation.CenterScreen;
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
        foreach(var name in new[]{"个性化中心","通用设置","显示设置","媒体设置","交互设置","我的插件","关于软件"})
        {
            var page=new StackPanel {Margin=new Thickness(16,0,24,20)};pages[name]=page;
            pageViews[name]=new ScrollViewer {Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            var button=new Button {Content=name,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,10,12,10),Margin=new Thickness(0,0,0,5),Background=Brushes.Transparent};
            button.Click+=(_,_)=>SelectPage(name);tabs[name]=button;navigation.Children.Add(button);
        }
        SelectPage("个性化中心");
        var pluginPage=pages["我的插件"];var pluginRows=new StackPanel();
        void RefreshPlugins()
        {
            pluginRows.Children.Clear();foreach(var id in activePlugins())
            {
                var row=new DockPanel{Margin=new Thickness(0,5,0,5)};foreach(var direction in new[]{1,-1}){var step=direction;var move=new Button{Content=step<0 ? "↑" : "↓",Padding=new Thickness(8,6,8,6),Margin=new Thickness(3,0,0,0)};DockPanel.SetDock(move,Dock.Right);move.Click+=(_,_)=>{movePlugin(id,step);RefreshPlugins();};row.Children.Add(move);}var remove=new Button{Content="卸载",Padding=new Thickness(10,6,10,6),Margin=new Thickness(3,0,0,0)};DockPanel.SetDock(remove,Dock.Right);remove.Click+=(_,_)=>{if(CardDialog.Show("卸载插件？","胶囊安装的包将删除；外部 DLL 只移出列表，原文件和个人设置保留。","卸载",destructive:true)==null)return;try{uninstallPlugin(id);RefreshPlugins();}catch(Exception error){Design.PresentMessage("卸载未完成",error.Message);}};row.Children.Add(remove);var disable=new Button{Content="停用",Padding=new Thickness(10,6,10,6)};DockPanel.SetDock(disable,Dock.Right);disable.Click+=(_,_)=>{disablePlugin(id);RefreshPlugins();};row.Children.Add(disable);row.Children.Add(new TextBlock{Text=id,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis});pluginRows.Children.Add(row);
            }
            if(pluginRows.Children.Count==0)pluginRows.Children.Add(new TextBlock{Text="尚未加载外部插件。",Margin=new Thickness(0,8,0,8)});
        }
        var pluginLoad=new Button{Content="安装插件包／加载 DLL…",Padding=new Thickness(12,10,12,10),Margin=new Thickness(0,12,0,12)};
        pluginLoad.Click+=(_,_)=>
        {
            var picker=new Microsoft.Win32.OpenFileDialog{Filter="灵动胶囊插件|*.zip;*.dll",CheckFileExists=true};if(picker.ShowDialog(this)!=true)return;
            if(CardDialog.Show("加载此插件？","插件代码会在本程序中运行，请仅加载信任的插件。ZIP 包安装到胶囊目录；单独 DLL 及依赖保留原位置。需要实现灵动胶囊 PluginApi。","加载")==null)return;
            try{loadPlugin(picker.FileName);RefreshPlugins();}catch(Exception error){Design.PresentMessage("插件未能加载",error.Message);}
        };
        pluginPage.Children.Add(pluginLoad);pluginPage.Children.Add(Design.Card(pluginRows));RefreshPlugins();
        var pluginErrors=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,10,0,0)};Design.Bind(pluginErrors,TextBlock.ForegroundProperty,"TextSecondary");pluginPage.Children.Add(pluginErrors);
        var priorPlugins=string.Join("|",activePlugins());var pluginTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};pluginTimer.Tick+=(_,_)=>{var current=string.Join("|",activePlugins());if(current!=priorPlugins){priorPlugins=current;RefreshPlugins();}pluginErrors.Text=string.Join("\n",settings.PluginFailures.Values);};pluginTimer.Start();Closed+=(_,_)=>pluginTimer.Stop();
        var panel=pages["个性化中心"];

        Heading(panel, "外观与动效");
        AddSelection(panel,"字体",()=>{var options=new Dictionary<string,string>{{"","系统默认（恢复默认）"}};foreach(var font in Fonts.SystemFontFamilies.OrderBy(font=>font.Source))options.TryAdd(font.Source,font.Source);return options;},settings.FontName,value=>{settings.FontName=value;Design.ApplyFont(value);FontFamily=Design.UserFont;},"选择本机字体；不打包或分发字体文件，找不到字形时由系统回退。");
        AddSelection(panel,"界面主题",()=>new Dictionary<string,string>{{"0","深色"},{"1","浅色"},{"2","跟随系统"}},settings.ThemeMode.ToString(),value=>{settings.ThemeMode=int.Parse(value);Design.ApplyTheme(settings.ThemeMode);foreach(var tab in tabs)if(tab.Value.Background!=Brushes.Transparent)tab.Value.SetResourceReference(Control.BackgroundProperty,"ButtonFill");},"设置、引导和弹窗即时切换；岛体材质主题仍在单独对齐。");
        var appearance = new StackPanel();
        AddSlider(appearance,"背景不透明度",0,100,settings.BackgroundOpacity,value=>settings.BackgroundOpacity=value,"百分比；仅调整岛体背景，不降低文字和按钮的不透明度。",1);
        AddSlider(appearance, "*全局上限／播放宽度", 80, 300, settings.Width, v => settings.Width = v, "逻辑像素；播放固定宽度，时钟、通知和详情均不超过此上限。较窄时内容会裁切。",5);
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
        var publishing=new StackPanel();AddCheck(publishing,"MSP 事件广播",settings.MspPublishEvents,value=>settings.MspPublishEvents=value,"默认关闭，需同时开启 MSP 接入。向已连接本机节点发送媒体变化和通知事件；隐私预览开启时不发送通知标题与正文。接收的 MSP 通知不会回广播。");panel.Children.Add(Design.Card(publishing));
        var mspOptions=new StackPanel();mspCheck=AddCheck(mspOptions,"MSP 实验性接入",settings.MspEnabled,value=>settings.MspEnabled=value,"默认关闭。开启后向本机共享目录宣告 lingdongcapsule 节点，允许发现它的本机 MSP 节点调用通知、媒体状态与控制；关闭后停止节点。主动广播另由事件广播开关控制。");panel.Children.Add(Design.Card(mspOptions));
        Heading(panel,"通知展示");
        var notificationOptions=new StackPanel();
        AddSlider(notificationOptions,"通知高度",40,120,settings.NotificationHeight,v=>settings.NotificationHeight=v,"逻辑像素；图标或完整内容需要时可自动增高。",1);
        AddSlider(notificationOptions,"*通知图标大小",18,settings.Width,settings.NotificationIconSize,v=>settings.NotificationIconSize=v,"显示时保留正文空间，不会挤占整个岛体。",1);
        AddSlider(notificationOptions,"*通知图标间距",0,20,settings.NotificationIconGap,v=>settings.NotificationIconGap=v,"图标与文字之间的逻辑像素。",1);
        AddSelection(notificationOptions,"通知内容模式",()=>new Dictionary<string,string>{{"0","缩略"},{"1","紧凑"},{"2","完整"}},settings.NotificationMode.ToString(),v=>settings.NotificationMode=int.Parse(v),"缩略显示标题与正文；紧凑增加侧边应用信息；完整增加应用名一行。");
        AddCheck(notificationOptions,"连续同源通知合并",settings.GroupNotifications,v=>settings.GroupNotifications=v,"同一应用在当前通知展示期间继续来消息时显示累计数量，仅展示最新内容。");
        AddCheck(notificationOptions,"点击展开通知",settings.ExpandNotifications,v=>settings.ExpandNotifications=v,"点击正文展开阅读；隐私预览开启时，仍需点击才能阅读正文。");
        AddCheck(notificationOptions,"*上滑收起通知",settings.SwipeNotifications,v=>settings.SwipeNotifications=v,"按住通知向上拖动 12 个逻辑像素即可收起。");
        quietCheck=AddCheck(notificationOptions,"*安静 30 分钟",settings.QuietUntil>DateTimeOffset.UtcNow,v=>settings.QuietUntil=v ? DateTimeOffset.UtcNow.AddMinutes(30) : default,"暂停岛内通知与提示音，到时自动恢复；不会补播期间通知，也不改 Windows 通知设置。");
        panel.Children.Add(Design.Card(notificationOptions));
        Heading(panel,"通知提示音");
        var soundOptions=new StackPanel();
        AddSlider(soundOptions,"独立音量",0,100,settings.NotificationSoundVolume,v=>settings.NotificationSoundVolume=v,"仅控制灵动胶囊提示音，不改系统主音量。",10);
        AddSelection(soundOptions,"提示音",NotificationSound.Options,settings.NotificationSoundKey,v=>settings.NotificationSoundKey=v,"14 种原创轻提示音，或选择自己的音频；不分发未经授权的系统或聊天软件音效。");
        var fileName=new TextBlock{Text=settings.NotificationSoundPath.Length>0 ? System.IO.Path.GetFileName(settings.NotificationSoundPath) : "尚未选择本地音频",FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,7,0,7)};Design.Bind(fileName,TextBlock.ForegroundProperty,"TextSecondary");soundOptions.Children.Add(fileName);
        var browseSound=new Button{Content="选择本地音频…",Padding=new Thickness(12,8,12,8)};
        browseSound.Click+=async(_,_)=>
        {
            var picker=new Microsoft.Win32.OpenFileDialog{Title="选择通知提示音",Filter="音频|*.wav;*.mp3;*.m4a;*.aac;*.wma;*.flac;*.aiff;*.aif",CheckFileExists=true,Multiselect=false};
            if(picker.ShowDialog(this)!=true)return;
            if(!NotificationSound.ValidFile(picker.FileName,out var error)){Design.PresentMessage("无法使用此音频",error);return;}
            browseSound.IsEnabled=false;
            try
            {
                if(!await NotificationSound.ValidateDurationAsync(picker.FileName)){Design.PresentMessage("无法使用此音频","请选择系统能解码、时长不超过 5 秒的音频。");return;}
                var folder=System.IO.Path.Combine(Settings.Folder,"Sounds");System.IO.Directory.CreateDirectory(folder);
                var destination=System.IO.Path.Combine(folder,"custom-"+Guid.NewGuid().ToString("N")+System.IO.Path.GetExtension(picker.FileName));
                System.IO.File.Copy(picker.FileName,destination);settings.NotificationSoundPath=destination;fileName.Text=System.IO.Path.GetFileName(picker.FileName);save();
                Design.PresentMessage("音频已保存","在提示音选项中选择“自选音频”即可使用。");
            }
            catch(Exception exception) when(exception is System.IO.IOException or UnauthorizedAccessException){Design.PresentMessage("音频未能保存","请检查文件是否仍然可读，以及保存目录权限。");}
            finally{browseSound.IsEnabled=true;}
        };
        soundOptions.Children.Add(browseSound);
        var previewSound=new Button{Content="试听",Margin=new Thickness(0,7,0,7),Padding=new Thickness(12,8,12,8)};
        previewSound.Click+=async(_,_)=>{previewSound.IsEnabled=false;try{if(!await soundPreview.PlayAsync(settings,preview:true))Design.PresentMessage("试听未能播放",soundPreview.Error.Length>0 ? soundPreview.Error : "请检查音频选择与输出设备。");}finally{previewSound.IsEnabled=true;}};
        soundOptions.Children.Add(previewSound);
        AddCheck(soundOptions,"通知提示音",settings.NotificationSoundEnabled,v=>settings.NotificationSoundEnabled=v,"默认关闭；同源合并后只在第一条播放，安静时段不播放。Windows 原通知声音由系统独立管理。");
        panel.Children.Add(Design.Card(soundOptions));

        panel=pages["显示设置"];
        Heading(panel,"显示位置与形态");
        var clockOptions=new StackPanel();
        AddSelection(clockOptions,"待机场景",()=>new Dictionary<string,string>{{"1","时间"},{"2","空白"},{"3","媒体控制"}},settings.StandbyScene.ToString(),value=>settings.StandbyScene=int.Parse(value),"待机时仅显示所选内容；通知仍可临时覆盖，媒体控制需要已有媒体会话。");
        AddCheck(clockOptions,"待机模式",settings.StandbyActive,value=>settings.StandbyActive=value,"临时使用单一场景，退出后恢复原有组件开关与顺序。");
        AddCheck(clockOptions,"双击切换待机",settings.StandbyDoubleClick,value=>settings.StandbyDoubleClick=value,"双击岛体内容区域切换待机；启用后单击会等待系统双击判定时间。");
        AddSelection(clockOptions,"空闲组件顺序",()=>new Dictionary<string,string>{{"clock","时钟 → 硬件"},{"hardware","硬件 → 时钟"}},settings.HardwareFirst ? "hardware" : "clock",value=>settings.HardwareFirst=value=="hardware","调整折叠态已启用的时钟与硬件组件排列；关闭的组件不占位置。");
        AddSelection(clockOptions,"时钟布局",()=>new Dictionary<string,string>{{"0","居中时钟"},{"1","时间与日期"}},settings.ClockLayout.ToString(),value=>settings.ClockLayout=int.Parse(value),"居中时钟对应当前定制组件；时间与日期按 HH:mm 与 MM/dd 显示，并根据内容收窄。");
        AddCheck(clockOptions,"显示时间日期",settings.ClockEnabled,value=>settings.ClockEnabled=value,"关闭后不显示时钟文本；仍保留空闲面板点击入口，不影响播放和通知。");
        AddCheck(clockOptions,"显示硬件占用",settings.HardwareEnabled,value=>settings.HardwareEnabled=value,"空闲折叠态每秒采样真实 CPU 与内存；无有效数据时显示横线，不显示虚构数值。媒体组合与排序仍在对齐中。");
        panel.Children.Add(Design.Card(clockOptions));
        var positionOptions=new StackPanel();
        AddSelection(positionOptions,"目标显示器",MonitorPlacement.Options,settings.MonitorId,v=>settings.MonitorId=v,"显示器断开时回到主屏，不修改其他软件设置。");
        AddSelection(positionOptions,"岛体形态",()=>new Dictionary<string,string>{{"1","灵动胶囊"},{"0","刘海"}},settings.IslandShape.ToString(),v=>settings.IslandShape=int.Parse(v),"刘海贴合屏幕上沿；胶囊保留顶部间距。");
        AddSlider(positionOptions,"刘海底部圆角",0,28,settings.BottomRadius,v=>settings.BottomRadius=v,"逻辑像素，仅刘海形态生效。",1);
        panel.Children.Add(Design.Card(positionOptions));
        var overviewOptions=new StackPanel();
        AddCheck(overviewOptions,"*空闲信息面板",settings.OverviewEnabled,v=>settings.OverviewEnabled=v,"空闲时单击时钟展开天气、网速、硬件状态和内存排行；也可从托盘打开。城市只在面板顶部编辑。");
        AddCheck(overviewOptions,"明日天气",settings.ShowTomorrow,v=>settings.ShowTomorrow=v,"空闲面板显示第二天预报，不影响今日天气。");
        panel.Children.Add(Design.Card(overviewOptions));
        panel=pages["媒体设置"];
        Heading(panel,"媒体识别");
        var mediaOptions=new StackPanel();
        AddCheck(mediaOptions,"媒体控制",settings.MediaEnabled,v=>settings.MediaEnabled=v,"接入系统媒体会话；关闭后停止媒体与歌词接入。");
        AddSelection(mediaOptions,"播放器类型",()=>new Dictionary<string,string>{{"other","其他软件 / 自动"},{"browser","浏览器媒体"},{"netease","网易云音乐"},{"qqmusic","QQ 音乐"},{"kugou","酷狗音乐"},{"spotify","Spotify"},{"apple","Apple Music"},{"echo","Echo Music"},{"lxmusic","LX Music"},{"justsolo","Just Solo（本机接入）"}},settings.MediaSourceKind,v=>settings.MediaSourceKind=v,"自动匹配时按软件类型筛选；手动指定软件后优先使用指定会话。");
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
        Heading(panel,"自动隐藏与穿透");
        var hiding=new StackPanel();
        AddCheck(hiding,"自动隐藏",settings.AutoHide,v=>settings.AutoHide=v,"总开关；下方三种触发条件可组合，通知与已展开面板优先保持可见。");
        AddCheck(hiding,"空闲时自动隐藏",settings.FocusAutoHide,v=>settings.FocusAutoHide=v,"需开启自动隐藏；没有媒体会话时收起，点击唤醒按钮恢复。");
        AddCheck(hiding,"暂停时自动隐藏",settings.PauseAutoHide,v=>settings.PauseAutoHide=v,"需开启自动隐藏；媒体暂停时收起，不改播放器状态。");
        AddCheck(hiding,"全屏时自动隐藏",settings.FullscreenAutoHide,v=>settings.FullscreenAutoHide=v,"需开启自动隐藏；系统报告全屏或演示状态时收起。");
        AddCheck(hiding,"穿透模式",settings.Passthrough,v=>settings.Passthrough=v,"悬停时淡出岛体并允许点击底层窗口；中央唤醒按钮恢复交互，通知和展开内容不会穿透。");
        panel.Children.Add(Design.Card(hiding));
        Heading(panel,"全局快捷键");
        var bindings=new StackPanel();
        AddCheck(bindings,"媒体快捷键",settings.MediaShortcutsEnabled,v=>settings.MediaShortcutsEnabled=v,"默认关闭。启用后可在其他软件中控制当前媒体；录制时临时撤下所有胶囊快捷键。");
        AddCheck(bindings,"岛体快捷键",settings.IslandShortcutEnabled,v=>settings.IslandShortcutEnabled=v,"独立于媒体开关，默认关闭。隐藏后也可从系统托盘唤回。");
        foreach(var action in Enum.GetValues<ShortcutAction>())
        {
            var row=new Grid{Margin=new Thickness(0,5,0,5)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var name=new TextBlock{Text=GlobalShortcuts.Label(action),VerticalAlignment=VerticalAlignment.Center};row.Children.Add(name);
            var record=new Button{Content=GlobalShortcuts.Format(settings.Shortcuts[action]),Padding=new Thickness(10,8,10,8),MinWidth=100,ToolTip="点击录制，Esc 取消，Backspace 清空"};
            record.Click+=(_,_)=>{if(ShortcutRecorder.Show(this,shortcuts,action)){record.Content=GlobalShortcuts.Format(settings.Shortcuts[action]);save();}};Grid.SetColumn(record,1);row.Children.Add(record);bindings.Children.Add(row);
        }
        var shortcutStatus=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,8,0,0)};Design.Bind(shortcutStatus,TextBlock.ForegroundProperty,"TextSecondary");bindings.Children.Add(shortcutStatus);
        var shortcutTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};shortcutTimer.Tick+=(_,_)=>shortcutStatus.Text=shortcuts.Error;shortcutTimer.Start();Closed+=(_,_)=>shortcutTimer.Stop();
        panel.Children.Add(Design.Card(bindings));
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
        var updateStatus=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,8,0,8)};Design.Bind(updateStatus,TextBlock.ForegroundProperty,"TextSecondary");
        var update=new Button{Content="检查更新",Padding=new Thickness(12,10,12,10),Margin=new Thickness(0,8,0,0)};
        var download=new Button{Content="查看新版本",Visibility=Visibility.Collapsed,Padding=new Thickness(12,10,12,10)};Uri? releaseLink=null;
        download.Click+=(_,_)=>{if(releaseLink!=null)System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(releaseLink.AbsoluteUri){UseShellExecute=true});};
        var updateCancellation=new CancellationTokenSource();Closed+=(_,_)=>{updateCancellation.Cancel();updateCancellation.Dispose();};
        update.Click+=async(_,_)=>
        {
            update.IsEnabled=false;download.Visibility=Visibility.Collapsed;updateStatus.Text="正在检查…";
            try{using var client=new System.Net.Http.HttpClient{Timeout=TimeSpan.FromSeconds(12)};var result=await ReleaseChecker.CheckAsync(client,typeof(SettingsWindow).Assembly.GetName().Version ?? new Version(0,1,0),updateCancellation.Token);if(updateCancellation.IsCancellationRequested)return;updateStatus.Text=result.Message;releaseLink=result.Link;download.Visibility=releaseLink==null ? Visibility.Collapsed : Visibility.Visible;}
            finally{update.IsEnabled=true;}
        };
        about.Children.Add(update);about.Children.Add(updateStatus);about.Children.Add(download);
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
        if(quietCheck!=null)quietCheck.IsChecked=settings.QuietUntil>DateTimeOffset.UtcNow;
        if(mspCheck!=null)mspCheck.IsChecked=settings.MspEnabled;
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
