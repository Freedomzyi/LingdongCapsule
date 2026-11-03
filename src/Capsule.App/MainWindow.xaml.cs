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
    private readonly NotificationService notifications = new();
    private readonly Settings settings;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private WelcomeWindow? welcomeWindow;
    private bool busy, closing, controlsOpen, lastNotificationsEnabled;
    private double desiredWidth = 104, desiredHeight = 35;
    private SettingsWindow? settingsWindow;
    private readonly OverviewView overview;
    private bool overviewOpen;
    private readonly DispatcherTimer panelCollapse=new();
    internal OverviewView LiveOverview=>overview;
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    public MainWindow(bool suppressOnboarding = false,Settings? initialSettings=null)
    {
        settings=initialSettings ?? Settings.Load();
        InitializeComponent();
        overview=new OverviewView(settings,SaveAndApplySettings);OverviewHost.Content=overview;
        MouseEnter+=(_,_)=>panelCollapse.Stop();
        MouseLeave+=(_,_)=>{if(overviewOpen || controlsOpen){panelCollapse.Interval=TimeSpan.FromMilliseconds(overviewOpen ? 900 : 3000);panelCollapse.Start();}};
        panelCollapse.Tick+=(_,_)=>{if(overview.DialogOpen || seeking)return;panelCollapse.Stop();if(!IsMouseOver){overviewOpen=false;controlsOpen=false;Render();}};
        LiveLyrics.FrameProvider=CurrentLyricFrame;
        InitializeMediaControls();
        SizeChanged+=(_,_)=>CenterOnMonitor();
        desiredHeight = settings.CompactHeight; Height = desiredHeight;
        if (Environment.GetCommandLineArgs().Contains("--inspect")) ShowInTaskbar = true;
        SourceInitialized += (_, _) => { var h = new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(h)?.AddHook(WindowHook); ApplyCapture(); };
        Loaded += async (_, _) =>
        {
            Position();
            using (var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/LingdongCapsule;component/Assets/icon.ico")).Stream) trayIcon = new System.Drawing.Icon(iconStream, 32, 32);
            tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "灵动胶囊", Visible = true };
            tray.MouseUp += (_, e) => { if (e.Button == Forms.MouseButtons.Right) Dispatcher.Invoke(ShowTrayMenu); };
            tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.PowerModeChanged += OnPowerChanged;
            timer.Tick += async (_, _) => await RefreshAsync(); timer.Start();
            await RefreshAsync();
            if (Environment.GetCommandLineArgs().Contains("--settings")) OpenSettings();
            else if (!suppressOnboarding && !settings.OnboardingCompleted && !Environment.GetCommandLineArgs().Contains("--startup")) ShowWelcome();
        };
        Closed += (_, _) => { closing = true; timer.Stop();panelCollapse.Stop();overview.Dispose();mediaTimer.Stop();audio.Dispose();lyricRequest?.Cancel();lyricRequest?.Dispose();lyricsClient.Dispose(); media.Dispose(); notifications.Dispose(); tray?.Dispose(); trayIcon?.Dispose(); settingsWindow?.Close(); welcomeWindow?.Close(); SystemEvents.DisplaySettingsChanged -= OnDisplayChanged; SystemEvents.PowerModeChanged -= OnPowerChanged; };
    }
    private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == 0x02E0 || msg == 0x007E) Dispatcher.BeginInvoke(Position);
        return IntPtr.Zero;
    }
    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Position);
    private void OnPowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(() => { state.Tick(DateTimeOffset.Now); notifications.ResetBaseline(); Position(); }); }
    private void Position()
    {
        BeginAnimation(WidthProperty, null); BeginAnimation(HeightProperty, null);
        Width = desiredWidth; Height = desiredHeight;
        BeginAnimation(LeftProperty, null);
        CenterOnMonitor();
    }
    private void CenterOnMonitor()=>MonitorPlacement.Center(new WindowInteropHelper(this).Handle,settings.MonitorId,Environment.GetCommandLineArgs().Any(arg=>arg is "--inspect" or "--render-media") ? 80 : settings.IslandShape==0 ? 0 : 8);
    private async Task RefreshAsync()
    {
        if (busy || closing) return;
        busy = true;
        try
        {
            media.Configure(settings.MediaEnabled,settings.MediaSourceKind,settings.ManualMediaSource);
            var latest = await media.ReadAsync();
            if (!latest.Available || (state.Media.Playing && !latest.Playing)) controlsOpen = false;
            state.UpdateMedia(latest);
            UpdateLyrics(latest);
            if (lastNotificationsEnabled != settings.NotificationsEnabled) { notifications.ResetBaseline(); lastNotificationsEnabled = settings.NotificationsEnabled; if (!lastNotificationsEnabled) state.Dismiss(); }
            if (settings.NotificationsEnabled) foreach (var n in await notifications.ReadAsync(settings.MutedSources)) { state.Show(n, DateTimeOffset.Now); controlsOpen = false; overviewOpen=false; }
            if (closing) return;
            state.Tick(DateTimeOffset.Now); Render();
            if(overviewOpen)await overview.TickAsync();
            settingsWindow?.UpdateStatus(media.Status+"\n"+(settings.LyricsEnabled ? lyricsClient.Status : "歌词已关闭"), settings.NotificationsEnabled ? notifications.Status : "通知监听已关闭", notifications.Sources);
        }
        finally { busy = false; }
    }
    private void Render()
    {
        Clock.Text = DateTime.Now.ToString("HH:mm");
        state.MediaClockSwitch=settings.MediaClockSwitch;
        var mode = state.Mode;
        if(!settings.OverviewEnabled)overviewOpen=false;
        var showOverview=overviewOpen && mode!=IslandMode.Notification;
        var showMedia = !showOverview && (mode == IslandMode.Media || (mode == IslandMode.Clock && controlsOpen && state.Media.Available));
        OverviewHost.Visibility=showOverview ? Visibility.Visible : Visibility.Collapsed;
        Clock.Visibility = mode == IslandMode.Clock && !showMedia && !showOverview ? Visibility.Visible : Visibility.Collapsed;
        MediaPanel.Visibility = showMedia ? Visibility.Visible : Visibility.Collapsed;
        NoticePanel.Visibility = mode == IslandMode.Notification ? Visibility.Visible : Visibility.Collapsed;
        Track.Text = state.Media.Title; Artist.Text = state.Media.Artist;
        CoverArt.Source = media.Cover; CoverFrame.Visibility = media.Cover is null ? Visibility.Collapsed : Visibility.Visible;
        Artist.Visibility = IsMediaExpanded ? Visibility.Visible : Visibility.Collapsed;
        var coverSize = IsMediaExpanded ? 38 : Math.Clamp(settings.CompactHeight - 10, 22, 40);
        if (CoverFrame.Width != coverSize) { CoverFrame.Width = CoverFrame.Height = coverSize; CoverArt.Clip = new System.Windows.Media.RectangleGeometry(new Rect(0, 0, coverSize, coverSize), 6, 6); }
        RenderLyrics();
        if (state.Notification is { } n)
        {
            NoticeApp.Text = n.App;
            NoticeTitle.Text = settings.PrivatePreview && !state.Expanded ? "新消息 · 点击查看" : n.Title;
            NoticeTitle.TextAlignment = state.Expanded ? TextAlignment.Left : TextAlignment.Center;
            NoticeBody.Text = n.Body;
            NoticeBodyScroll.Visibility = state.Expanded ? Visibility.Visible : Visibility.Collapsed;
        }
        else { NoticeApp.Text = ""; NoticeTitle.Text = ""; NoticeBody.Text = ""; }
        var width = mode == IslandMode.Notification || showMedia || showOverview ? Math.Min(settings.Width, Math.Max(104, SystemParameters.WorkArea.Width - 16)) : 104;
        var height = mode == IslandMode.Notification ? (state.Expanded ? 190 : 64) : showOverview ? settings.ShowTomorrow ? 278 : 258 : showMedia && IsMediaExpanded ? media.Duration>TimeSpan.Zero ? 158 : 130 : settings.CompactHeight;
        MediaPanel.VerticalAlignment=IsMediaExpanded ? VerticalAlignment.Top : VerticalAlignment.Center;
        MediaPanel.Margin=IsMediaExpanded ? new Thickness(0,19,0,0) : new Thickness(0);
        Shell.Padding = mode == IslandMode.Notification || IsMediaExpanded || showOverview ? new Thickness(14, 7, 14, 7) : LiveLyrics.Visibility==Visibility.Visible ? new Thickness(12,1,12,1) : new Thickness(12, 4, 12, 4);
        Shell.CornerRadius = settings.IslandShape==0 ? new CornerRadius(0,0,settings.BottomRadius,settings.BottomRadius) : new CornerRadius(height > 100 ? 22 : height / 2);
        Topmost=settings.Topmost;
        Animate(width, height);
        RenderMediaControls();
    }
    private void Animate(double width, double height)
    {
        if (desiredWidth == width && desiredHeight == height) return;
        desiredWidth = width; desiredHeight = height;
        var duration = TimeSpan.FromMilliseconds(settings.MotionMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(WidthProperty, new DoubleAnimation(width, duration) { EasingFunction = ease });
        BeginAnimation(HeightProperty, new DoubleAnimation(height, duration) { EasingFunction = ease });
    }
    private void OnIslandClick(object sender, MouseButtonEventArgs e)
    {
        if (state.Notification is not null) { if (state.Expanded) Dismiss(); else state.Expand(DateTimeOffset.Now); }
        else if (state.Media.Available) controlsOpen = !controlsOpen;
        else if(settings.OverviewEnabled)OpenOverview();
        else OpenSettings();
        Render();
    }
    private void OnRightClick(object sender, MouseButtonEventArgs e) => OpenSettings();
    private async void OnControl(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var button = (System.Windows.Controls.Button)sender; button.IsEnabled = false;
        try { if (!await media.ControlAsync((string)button.Tag)) Design.PresentMessage("播放器未响应", "播放器暂未响应此操作，请稍后重试。"); await RefreshAsync(); }
        finally { button.IsEnabled = true; }
    }
    private void Dismiss() { state.Dismiss(); controlsOpen = false; overviewOpen=false; Render(); }
    internal void OpenOverview(){if(!settings.OverviewEnabled)return;overviewOpen=true;controlsOpen=false;Render();_=overview.TickAsync();}
    private void TestNotice() { overviewOpen=false;state.Show(new("self", "灵动胶囊 · 测试", "通知交互已就绪", "这是一条本地测试通知。真实系统通知需要授权；点击展开，再次点击收起。"), DateTimeOffset.Now); Render(); }
    private void ApplyCapture() { if (!SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, settings.ShowInCapture ? 0u : 0x11u)) { /* Supported capture tools only; no global capture guarantee. */ } }
    private void OpenSettings()
    {
        if (settingsWindow is not null) { settingsWindow.Activate(); return; }
        settingsWindow = new SettingsWindow(settings, async () => { var ok = await notifications.RequestAsync(); settings.NotificationsEnabled = ok; settings.Save(); return notifications.Status; }, SaveAndApplySettings, TestNotice, ConfigureStartup, ShowWelcome,()=>media.Sources);
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
    }
    private void SaveAndApplySettings()
    {
        try{settings.Save();}catch(Exception exception){Design.PresentMessage("设置未能保存",exception.Message);}
        media.Configure(settings.MediaEnabled,settings.MediaSourceKind,settings.ManualMediaSource);
        if(!settings.MediaEnabled)state.UpdateMedia(new("","",false,false));
        ApplyCapture();UpdateLyrics(state.Media);Render();CenterOnMonitor();_=RefreshAsync();
    }
    private void ConfigureStartup(bool enabled) { StartupService.SetEnabled(enabled); settings.StartWithWindows = enabled; settings.Save(); }
    private void ShowTrayMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint, PlacementTarget = this };
        void Add(string title, Action action) { var item = new System.Windows.Controls.MenuItem { Header = title }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("设置", OpenSettings);if(settings.OverviewEnabled)Add("空闲信息面板",OpenOverview); Add("试一条通知", TestNotice); Add("使用指南", ShowWelcome); Add("收起", Dismiss); Add("退出", Close); menu.IsOpen = true;
    }
    private void ShowWelcome()
    {
        if (welcomeWindow is not null) { welcomeWindow.Activate(); return; }
        welcomeWindow = new WelcomeWindow(StartupService.IsEnabled, ConfigureStartup, OpenSettings, () => { settings.OnboardingCompleted = true; settings.Save(); });
        welcomeWindow.Closed += (_, _) => welcomeWindow = null; welcomeWindow.Show();
    }
}
