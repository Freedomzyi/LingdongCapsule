using Capsule.Core;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
namespace Capsule.App;

public partial class MainWindow : Window
{
    private readonly IslandState state = new();
    internal IslandState LiveState => state;
    private readonly MediaService media = new();
    private readonly NotificationService notifications;
    private readonly Settings settings;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private WelcomeWindow? welcomeWindow;
    private bool busy, closing, controlsOpen, lastNotificationsEnabled;
    private double desiredWidth = 104, desiredHeight = 35;
    internal double ActiveVisualScale{get;private set;}=1;
    private readonly System.Windows.Media.ScaleTransform islandScale=new(1,1);
    private SettingsWindow? settingsWindow;
    private readonly OverviewView overview;
    private readonly OverviewService compactHardware=new();
    private long nextHardwareSample;
    private readonly DispatcherTimer islandClickDelay=new();
    private bool skipIslandRelease;
    internal OverviewSample? CompactHardwareSample{get;private set;}
    private bool overviewOpen;
    private readonly DispatcherTimer panelCollapse=new();
    internal OverviewView LiveOverview=>overview;
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    public MainWindow(bool suppressOnboarding = false,Settings? initialSettings=null,Uri? localSoloEndpoint=null,string? localMspDirectory=null,string? localPluginDirectory=null,Action? pluginSettingsWriter=null,System.Net.Http.HttpClient? coverMetadata=null,System.Net.Http.HttpClient? coverPictures=null)
    {
        coverClient=new(coverMetadata,coverPictures);
        settings=initialSettings ?? Settings.Load();
        pluginDirectory=localPluginDirectory ?? System.IO.Path.Combine(Settings.Folder,"Plugins");
        writePluginSettings=pluginSettingsWriter ?? settings.Save;
        notifications=new NotificationService(persistSources:initialSettings==null);
        mspDirectory=localMspDirectory ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".msp","nodes");
        solo=new SoloConnection(localSoloEndpoint);
        Design.ApplyTheme(settings.ThemeMode);
        shortcuts=new GlobalShortcuts(settings);
        InitializeComponent();
        IslandRoot.LayoutTransform=islandScale;
        InitializeComposite();
        InitializeMaterial();
        Closed+=(_,_)=>solo.Dispose();
        ApplyUserFont();
        InitializeVisibility();
        InitializeMsp();
        islandClickDelay.Interval=TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime);
        islandClickDelay.Tick+=(_,_)=>{islandClickDelay.Stop();if(!closing)HandleIslandClick();};
        Closed+=(_,_)=>islandClickDelay.Stop();
        Shell.PreviewMouseLeftButtonDown+=(_,args)=>
        {
            if(settings.StandbyDoubleClick && state.Notification==null && args.ClickCount==2 && (ReferenceEquals(args.OriginalSource,Shell) || ReferenceEquals(args.OriginalSource,ClockTime) || ReferenceEquals(args.OriginalSource,ClockDate)))
            {islandClickDelay.Stop();skipIslandRelease=true;var previous=settings.StandbyActive;SetStandby(!previous);try{SaveAndApplySettings();}catch(Exception error){SetStandby(previous);Design.PresentMessage("设置未能保存",error.Message);}args.Handled=true;}
        };
        overview=new OverviewView(settings,SaveAndApplySettings);OverviewHost.Content=overview;
        InitializePlugins(initialSettings==null);
        MouseEnter+=(_,_)=>panelCollapse.Stop();
        MouseLeave+=(_,_)=>{if(overviewOpen || controlsOpen){panelCollapse.Interval=TimeSpan.FromMilliseconds(overviewOpen ? 900 : 3000);panelCollapse.Start();}};
        panelCollapse.Tick+=(_,_)=>{if(overview.DialogOpen || seeking)return;panelCollapse.Stop();if(!PointerOverIsland()){overviewOpen=false;controlsOpen=false;Render();}};
        LiveLyrics.FrameProvider=CurrentLyricFrame;
        InitializeMediaControls();
        InitializeCovers();
        media.Changed+=OnNativeMediaChanged;Closed+=(_,_)=>media.Changed-=OnNativeMediaChanged;
        InitializeNotifications();
        SizeChanged+=(_,_)=>{if(!SizeTransitionActive)CenterOnMonitor();};
        desiredHeight = settings.CompactHeight; Height = desiredHeight;
        if (Environment.GetCommandLineArgs().Contains("--inspect")) ShowInTaskbar = true;
        SourceInitialized += (_, _) => { var h = new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(h)?.AddHook(WindowHook);shortcuts.Attach(h); ApplyCapture(); };
        Loaded += async (_, _) =>
        {
            Position();
            using (var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/LingdongCapsule;component/Assets/icon.ico")).Stream) trayIcon = new System.Drawing.Icon(iconStream, 32, 32);
            tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "灵动胶囊", Visible = true };
            tray.MouseUp += (_, e) => { if (e.Button == Forms.MouseButtons.Right) Dispatcher.Invoke(ShowTrayMenu); };
            tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
            if(settings.RecoveryMessage.Length>0)tray.ShowBalloonTip(8000,"设置恢复提示",settings.RecoveryMessage,Forms.ToolTipIcon.Warning);
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.UserPreferenceChanged+=OnThemePreferenceChanged;
            SystemEvents.PowerModeChanged += OnPowerChanged;
            timer.Tick += async (_, _) => await RefreshAsync(); timer.Start();
            await RefreshAsync();
            ApplyMsp();
            if (Environment.GetCommandLineArgs().Contains("--settings")) OpenSettings();
            else if (!suppressOnboarding && !settings.OnboardingCompleted && !Environment.GetCommandLineArgs().Contains("--startup")) ShowWelcome();
        };
        Closed += (_, _) => { closing = true;visibilityProbe.Stop();shortcuts.Dispose(); timer.Stop();panelCollapse.Stop();notificationSound.Dispose();overview.Dispose();mediaTimer.Stop();audio.Dispose();lyricRequest?.Cancel();lyricRequest?.Dispose();lyricsClient.Dispose(); media.Dispose(); notifications.Dispose(); tray?.Dispose(); trayIcon?.Dispose(); settingsWindow?.Close(); welcomeWindow?.Close(); SystemEvents.UserPreferenceChanged-=OnThemePreferenceChanged;SystemEvents.DisplaySettingsChanged -= OnDisplayChanged; SystemEvents.PowerModeChanged -= OnPowerChanged; };
        Closed+=(_,_)=>StopSizeTransition();
    }
    private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == 0x02E0 || msg == 0x007E) Dispatcher.BeginInvoke(()=>{Render();Position();});
        if(msg==0x0312 && shortcuts.Handle(wp.ToInt32()))handled=true;
        if(msg==0x0084 && PassThroughHit(lp)){handled=true;return new IntPtr(-1);}
        return IntPtr.Zero;
    }
    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(()=>{if(!closing){Render();Position();}});
    private void OnThemePreferenceChanged(object sender,UserPreferenceChangedEventArgs e){if(!closing && settings.ThemeMode==2)Dispatcher.BeginInvoke(()=>Design.ApplyTheme(2));}
    private void OnPowerChanged(object sender, PowerModeChangedEventArgs e) { (Application.Current as App)?.Diagnostics?.Power(e.Mode.ToString());if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(() => { media.Invalidate();state.Tick(DateTimeOffset.Now); notifications.ResetBaseline(); Position();_=RefreshAsync(); }); }
    private void OnNativeMediaChanged(){if(!closing && !Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(async()=>await RefreshAsync());}
    private void Position()
    {
        StopSizeTransition();
        BeginAnimation(WidthProperty, null); BeginAnimation(HeightProperty, null);
        SetSurfaceSize(desiredWidth,desiredHeight);
        Width = desiredWidth; Height = desiredHeight;
        BeginAnimation(LeftProperty, null);
        CenterOnMonitor();
    }
    private void CenterOnMonitor()=>MonitorPlacement.Center(new WindowInteropHelper(this).Handle,settings.MonitorId,Environment.GetCommandLineArgs().Any(arg=>arg is "--inspect" or "--render-media") ? 80 : settings.IslandShape==0 ? 0 : 8*ActiveVisualScale);
    private async Task RefreshAsync()
    {
        if (busy || closing) return;
        busy = true;
        try
        {
            media.Configure(settings.MediaEnabled,settings.MediaSourceKind,settings.ManualMediaSource);
            var latest = await media.ReadAsync();
            if(closing)return;
            timer.Interval=TimeSpan.FromMilliseconds(media.PollMilliseconds);
            var useSolo=SoloSourceSelected(latest.SourceId);solo.Configure(settings.MediaEnabled && useSolo);
            if(useSolo)
            {
                var local=solo.Snapshot();
                if(local.Connected)latest=latest.Available ? latest with{Playing=local.Playing} : new MediaSnapshot("Just Solo","",local.Playing,true,SourceId:"justsolo");
            }
            if (!latest.Available || (state.Media.Playing && !latest.Playing)) controlsOpen = false;
            state.UpdateMedia(latest);
            if(settings.MspPublishEvents)msp.PublishMedia(latest);
            UpdateLyrics(latest);
            UpdateCovers(latest);
            if (lastNotificationsEnabled != settings.NotificationsEnabled) { notifications.ResetBaseline(); lastNotificationsEnabled = settings.NotificationsEnabled; if (!lastNotificationsEnabled) state.Dismiss(); }
            if (settings.NotificationsEnabled) foreach (var n in await notifications.ReadAsync(settings.MutedSources)) AcceptNotification(n,DateTimeOffset.Now);
            if (closing) return;
            state.Tick(DateTimeOffset.Now); Render();
            (Application.Current as App)?.Diagnostics?.Observe(state.Mode,visibilityResult.PassThrough,state.Media.Playing,plugins.ActiveIds.Count,media.SourceId.StartsWith("LingdongCapsule.Owned.",StringComparison.OrdinalIgnoreCase));
            plugins.Update(new(DateTimeOffset.Now,Design.IsDark,new(state.Media.Title,state.Media.Artist,state.Media.Available,state.Media.Playing)),modulesVisible && !settings.StandbyActive && !visibilityResult.PassThrough);
            QuarantineFailedPlugins();
            if(overviewOpen)await overview.TickAsync();
            var localStatus=solo.Snapshot();
            var mediaStatus=!settings.MediaEnabled ? "媒体已关闭" : SoloSelected ? localStatus.Connected ? "Just Solo 本机已连接" : media.SourceId.Length>0 ? "Just Solo 原生媒体已连接 · 本机歌词服务未连接" : "等待 Just Solo 本机服务（127.0.0.1:47290）" : media.Status;
            var lyricStatus=!settings.LyricsEnabled ? "歌词已关闭" : SoloSelected && localStatus.Connected && localStatus.Lyrics.Lines.Count>0 ? "歌词由 Just Solo 本机服务提供" : lyricKey.Length==0 ? "等待可匹配的音乐信息" : lyricsClient.Status;
            settingsWindow?.UpdateStatus(mediaStatus+"\n"+lyricStatus+"\n"+MaterialStatus+"\n"+(msp.Error.Length>0 ? msp.Error : msp.Enabled ? "MSP 本机节点已启用" : "MSP 已关闭"), settings.NotificationsEnabled ? notifications.Status : "通知监听已关闭", notifications.Sources);
        }
        finally { busy = false; }
    }
    private void Render()
    {
        var clockNow=DateTime.Now;
        ClockTime.Text=settings.ClockEnabled || settings.StandbyActive && settings.StandbyScene==1 ? clockNow.ToString("HH:mm") : "";
        ClockDate.Text=clockNow.ToString("MM/dd");
        ClockDate.Visibility=settings.ClockEnabled && settings.ClockLayout==1 ? Visibility.Visible : Visibility.Collapsed;
        HardwarePanel.Visibility=settings.HardwareEnabled && !settings.StandbyActive ? Visibility.Visible : Visibility.Collapsed;
        state.MediaClockSwitch=settings.MediaClockSwitch;
        state.SetQuietUntil(settings.QuietUntil,DateTimeOffset.Now);
        if(!settings.NotificationsEnabled || !settings.NotificationSoundEnabled || state.IsQuiet(DateTimeOffset.Now))notificationSound.StopPlayback();
        var mode = state.Mode;
        overview.SetEnabled(settings.OverviewEnabled);if(!settings.OverviewEnabled)overviewOpen=false;
        var showOverview=overviewOpen && mode!=IslandMode.Notification;
        var showMedia = !showOverview && (mode == IslandMode.Media || (mode == IslandMode.Clock && controlsOpen && state.Media.Available));
        if(settings.StandbyActive && mode!=IslandMode.Notification && !showOverview)showMedia=settings.StandbyScene==3 && state.Media.Available;
        var canSwitch=state.Media.Available && settings.OverviewEnabled && mode!=IslandMode.Notification && (showOverview || showMedia && controlsOpen);
        InformationSwitch.Visibility=canSwitch ? Visibility.Visible : Visibility.Collapsed;
        InformationSwitchGlyph.Media=showOverview;
        InformationSwitch.ToolTip=showOverview ? "返回媒体" : "系统信息";
        System.Windows.Automation.AutomationProperties.SetName(InformationSwitch,showOverview ? "返回媒体" : "查看系统信息");
        compactLayout.Margin=new Thickness(0,0,canSwitch && !showOverview && !IsMediaExpanded ? 30 : 0,0);
        OverviewHost.Margin=new Thickness(0,canSwitch && showOverview ? 30 : 0,0,0);
        OverviewHost.Visibility=showOverview ? Visibility.Visible : Visibility.Collapsed;
        Clock.Visibility = mode == IslandMode.Clock && !showMedia && !showOverview ? Visibility.Visible : Visibility.Collapsed;
        var combined=settings.CompositeEnabled && showMedia && !IsMediaExpanded && !settings.StandbyActive && mode!=IslandMode.Notification;
        if(combined)Clock.Visibility=Visibility.Visible;
        if(settings.StandbyActive && mode!=IslandMode.Notification && !showOverview)Clock.Visibility=settings.StandbyScene==1 ? Visibility.Visible : Visibility.Collapsed;
        var viewportLimit=MonitorPlacement.WidthLimit(new WindowInteropHelper(this).Handle,settings.MonitorId);
        SetVisualScale(MonitorPlacement.VisualScale(new WindowInteropHelper(this).Handle,settings.MonitorId,settings.VisualScale));
        if(IsLoaded && Width>viewportLimit){BeginAnimation(WidthProperty,null);Width=viewportLimit;desiredWidth=Math.Min(desiredWidth,viewportLimit);}
        var widthLimit=Math.Min(settings.Width,viewportLimit/ActiveVisualScale);
        RenderComposite(showMedia,combined,widthLimit);
        if(HardwarePanel.Visibility==Visibility.Visible && modulesVisible && !visibilityResult.PassThrough && Environment.TickCount64>=nextHardwareSample)
        {
            nextHardwareSample=Environment.TickCount64+1000;var sample=compactHardware.Sample();CompactHardwareSample=sample;
            CpuUsage.Text=sample.Cpu is {} cpu ? $"CPU {cpu:0}%" : "CPU —";RamUsage.Text=sample.Memory is {} ram ? $"RAM {ram}%" : "RAM —";
            CpuBar.Value=sample.Cpu ?? 0;RamBar.Value=sample.Memory ?? 0;
        }
        MediaPanel.Visibility = showMedia ? Visibility.Visible : Visibility.Collapsed;
        NoticePanel.Visibility = mode == IslandMode.Notification ? Visibility.Visible : Visibility.Collapsed;
        Track.Text = state.Media.Title; Artist.Text = state.Media.Artist;
        var artwork=EffectiveCover;CoverArt.Source=artwork;CoverFrame.Visibility=artwork is null ? Visibility.Collapsed : Visibility.Visible;
        Artist.Visibility = IsMediaExpanded && state.Media.Artist.Length>0 ? Visibility.Visible : Visibility.Collapsed;
        var coverSize = IsMediaExpanded ? 38 : Math.Clamp(settings.CompactHeight - 10, 22, 40);
        if (CoverFrame.Width != coverSize) { CoverFrame.Width = CoverFrame.Height = coverSize; CoverArt.Clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, coverSize, coverSize), 6, 6); }
        RenderLyrics();
        var width = mode == IslandMode.Notification || showMedia || showOverview ? widthLimit : Math.Min(104,widthLimit);
        if(modulesVisible && !showMedia && !showOverview)width=Math.Min(widthLimit,Math.Max(104,compactNaturalWidth+26));
        if(mode==IslandMode.Notification)width=NotificationWidth(width);
        var notificationHeight=RenderNotification(width);
        var height = mode == IslandMode.Notification ? notificationHeight : showOverview ? (settings.ShowTomorrow ? 278 : 258)+(canSwitch ? 30 : 0) : showMedia && IsMediaExpanded ? media.Duration>TimeSpan.Zero ? 158 : 130 : settings.CompactHeight;
        MediaPanel.VerticalAlignment=IsMediaExpanded ? VerticalAlignment.Top : VerticalAlignment.Center;
        MediaPanel.Margin=IsMediaExpanded ? new Thickness(0,19,0,0) : new Thickness(0);
        Shell.Padding = mode == IslandMode.Notification || IsMediaExpanded || showOverview ? new Thickness(14, 7, 14, 7) : LiveLyrics.Visibility==Visibility.Visible ? new Thickness(12,1,12,1) : new Thickness(12, 4, 12, 4);
        if(showMedia && IsMediaExpanded)
        {
            // Measure the current controls before choosing the expanded height.
            // A fixed height can crop the 40px hover circles after header/artwork changes.
            PrepareMediaLayout();
            MediaPanel.Measure(new Size(Math.Max(0,width-Shell.Padding.Left-Shell.Padding.Right-Shell.BorderThickness.Left-Shell.BorderThickness.Right),double.PositiveInfinity));
            height=Math.Max(height,Math.Ceiling(MediaPanel.DesiredSize.Height+Shell.Padding.Top+Shell.Padding.Bottom+Shell.BorderThickness.Top+Shell.BorderThickness.Bottom));
        }
        Shell.CornerRadius = settings.IslandShape==0 ? new CornerRadius(0,0,settings.BottomRadius,settings.BottomRadius) : new CornerRadius(height > 100 ? 22 : height / 2);
        Topmost=settings.Topmost;
        Animate(width*ActiveVisualScale, height*ActiveVisualScale);
        FadePresentation(showOverview ? 1 : mode==IslandMode.Notification ? 2 : showMedia ? IsMediaExpanded ? 4 : 3 : 0);
        UpdateIslandVisibility();
        RenderMediaControls();
        ApplyIslandTheme();
        UpdateMaterial();
    }
    private int lastPresentation=-1;
    private void FadePresentation(int presentation)
    {
        if(lastPresentation==presentation)return;
        var first=lastPresentation<0;lastPresentation=presentation;if(first)return;
        FrameworkElement incoming=presentation==1 ? OverviewHost : presentation==2 ? NoticePanel : compactLayout;
        incoming.BeginAnimation(OpacityProperty,null);incoming.Opacity=0;
        incoming.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(Math.Min(200,settings.MotionMs*.55))){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
    }
    private void OnInformationSwitch(object sender,RoutedEventArgs args)
    {
        args.Handled=true;panelCollapse.Stop();
        if(overviewOpen){overviewOpen=false;Render();}else OpenOverview();
    }
    private void OnIslandClick(object sender, MouseButtonEventArgs e)
    {
        if(skipIslandRelease){skipIslandRelease=false;e.Handled=true;return;}
        if(state.Notification==null && Within(e.OriginalSource,MediaPanel) && (IsMediaExpanded || settings.ReturnToPlayer)){e.Handled=true;return;}
        if(settings.StandbyDoubleClick && state.Notification==null && (ReferenceEquals(e.OriginalSource,Shell) || ReferenceEquals(e.OriginalSource,ClockTime) || ReferenceEquals(e.OriginalSource,ClockDate))){islandClickDelay.Stop();islandClickDelay.Start();return;}
        HandleIslandClick();
    }
    internal void SetStandby(bool active){settings.StandbyActive=active;controlsOpen=false;overviewOpen=false;Render();}
    private void HandleIslandClick()
    {
        if (state.Notification is {} notice)
        {
            if(state.Expanded)Dismiss();
            else if(settings.PrivatePreview || settings.ExpandNotifications){NoticeBodyScroll.ScrollToTop();state.Expand(DateTimeOffset.Now);}
            else if(AppActivation.Open(notice.SourceId))Dismiss();
        }
        else if (state.Media.Available) {controlsOpen = !controlsOpen;}
        else if(settings.OverviewEnabled)OpenOverview();
        else OpenSettings();
        Render();
    }
    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled=true;
        if(state.Notification==null && overviewOpen){overviewOpen=false;Render();return;}
        var onMedia=Within(e.OriginalSource,MediaPanel);
        if(state.Notification==null && onMedia && state.Media.Available && settings.ExpandedMediaControls && settings.ReturnToPlayer && !IsMediaExpanded)
        {controlsOpen=true;Render();return;}
        OpenSettings();
        if(onMedia && state.Notification==null)settingsWindow?.SelectPage("媒体设置");
        else if(Within(e.OriginalSource,Clock) || Within(e.OriginalSource,HardwarePanel))settingsWindow?.SelectPage("显示设置");
    }
    private static bool Within(object source,DependencyObject target)
    {
        for(var item=source as DependencyObject;item!=null;item=item is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item))
            if(ReferenceEquals(item,target))return true;
        return false;
    }
    private async void OnControl(object sender, RoutedEventArgs e)
    {
        e.Handled = true;if(closing)return;
        var button = (System.Windows.Controls.Button)sender; button.IsEnabled = false;
        try { var changed=await media.ControlAsync((string)button.Tag);if(closing)return;if(!changed)Design.PresentMessage("播放器未响应", "播放器暂未响应此操作，请稍后重试。");await RefreshAsync(); }
        finally { if(!closing)button.IsEnabled = true; }
    }
    private void Dismiss() { state.Dismiss(); controlsOpen = false; overviewOpen=false; Render(); }
    internal void OpenOverview(){if(!settings.OverviewEnabled)return;WakeIsland();overviewOpen=true;controlsOpen=false;Render();_=overview.TickAsync();}
    private uint testNoticeId;
    private void TestNotice() { if(!AcceptNotification(new("self", "灵动胶囊 · 测试", "通知交互已就绪", "这是一条本地测试通知。真实系统通知需要授权；点击展开，再次点击收起。",++testNoticeId), DateTimeOffset.Now))Design.PresentMessage("通知暂未显示","请检查通知开关或安静时段。"); }
    private void ApplyCapture() { if (!SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, settings.ShowInCapture ? 0u : 0x11u)) { /* Supported capture tools only; no global capture guarantee. */ } }
    internal void OpenSettings()
    {
        if (closing) return;
        if (settingsWindow is not null)
        {
            if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal;
            settingsWindow.Show(); settingsWindow.Activate(); return;
        }
        settingsWindow = new SettingsWindow(settings, () => AuthorizeNotificationsAsync(notifications.RequestAsync), SaveAndApplySettings, TestNotice, ConfigureStartup, ShowWelcome,()=>media.Sources,shortcuts,file=>LoadPlugin(file),()=>plugins.ActiveIds.ToArray(),DisablePlugin,UninstallPlugin,InstalledPlugins,RemoveInstalledPlugin,InstallMarketPackage,DisplayComponents,MoveDisplayComponent,fontPreferenceWriter:()=>{settings.Save();ApplyUserFont();});
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
    }
    internal async Task<string> AuthorizeNotificationsAsync(Func<Task<bool>> request)
    {
        var allowed=await request();var previous=settings.NotificationsEnabled;
        settings.NotificationsEnabled=allowed;
        try{SaveAndApplySettings();}catch{settings.NotificationsEnabled=previous;throw;}
        return notifications.Status;
    }
    private void SaveAndApplySettings()
        =>ApplySettings(true);
    private void ApplySettings(bool persist)
    {
        // Persist before starting external services or changing live behavior. Callers
        // must see failure so their control and in-memory preference can roll back.
        if(persist)writePluginSettings();
        var requestedMsp=settings.MspEnabled;
        ApplyMsp();
        if(persist && requestedMsp!=settings.MspEnabled)writePluginSettings();
        ApplyUserFont();
        solo.Configure(settings.MediaEnabled && SoloSelected);
        Design.ApplyTheme(settings.ThemeMode);
        media.Configure(settings.MediaEnabled,settings.MediaSourceKind,settings.ManualMediaSource);
        if(!settings.MediaEnabled)state.UpdateMedia(new("","",false,false));
        if(!settings.NotificationsEnabled)state.Dismiss();
        if(!settings.SystemNotificationImagesEnabled && state.Notification is {} currentNotice && currentNotice.CreatedAt!=default)noticeImageRequest.Cancel();
        shortcuts.Apply();
        ApplyCapture();UpdateLyrics(state.Media);UpdateCovers(state.Media);Render();CenterOnMonitor();_=RefreshAsync();
    }
    private void ApplyUserFont(){Design.ApplyFont(settings.FontName,settings.FontFilePath);FontFamily=Design.UserFont;LiveLyrics.SetFont(Design.UserFont);}
    private void ConfigureStartup(bool enabled)
    {
        var previous=settings.StartWithWindows;settings.StartWithWindows=enabled;
        try{StartupService.SetEnabledTransactional(enabled,settings.Save);}
        catch{settings.StartWithWindows=previous;throw;}
    }
    private void ShowTrayMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint, PlacementTarget = this };
        void Add(string title, Action action) { var item = new System.Windows.Controls.MenuItem { Header = title }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("设置", OpenSettings);Add(manuallyHidden ? "唤出灵动胶囊" : "隐藏灵动胶囊",()=>SetIslandHidden(!manuallyHidden));if(settings.OverviewEnabled)Add("空闲信息面板",OpenOverview); Add("试一条通知", TestNotice); Add("使用指南", ShowWelcome); Add("收起", Dismiss); Add("退出", Close); menu.IsOpen = true;
    }
    private void ShowWelcome()
    {
        if(closing)return;
        if (welcomeWindow is not null) { if(welcomeWindow.WindowState==WindowState.Minimized)welcomeWindow.WindowState=WindowState.Normal;welcomeWindow.Show();welcomeWindow.Activate(); return; }
        bool? startupState=null;try{startupState=StartupService.IsEnabled;}catch(Exception){ /* The guide remains usable without changing unreadable startup state. */ }
        welcomeWindow = new WelcomeWindow(WelcomeOptions.Read(settings),startupState,(choices,startup)=>
        {
            WelcomeOptions.Commit(settings,choices,startup,settings.Save,(enabled,save)=>
            {if(StartupService.IsEnabled==enabled)save();else StartupService.SetEnabledTransactional(enabled,save);});
            ApplySettings(false);
            settingsWindow?.RefreshPreferences();
        },OpenSettings);
        welcomeWindow.Closed += (_, _) => welcomeWindow = null; welcomeWindow.Show();
    }
}
