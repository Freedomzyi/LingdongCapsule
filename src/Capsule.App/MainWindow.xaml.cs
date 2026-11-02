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
    private readonly MediaService media = new();
    private readonly NotificationService notifications = new();
    private readonly Settings settings = Settings.Load();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Forms.NotifyIcon? tray;
    private bool busy, closing, controlsOpen;
    private double desiredWidth = 104, desiredHeight = 40;
    private SettingsWindow? settingsWindow;
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    public MainWindow()
    {
        InitializeComponent();
        if (Environment.GetCommandLineArgs().Contains("--inspect")) ShowInTaskbar = true;
        SourceInitialized += (_, _) => { var h = new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(h)?.AddHook(WindowHook); ApplyCapture(); };
        Loaded += async (_, _) =>
        {
            Position();
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(OpenSettings));
            menu.Items.Add("测试通知", null, (_, _) => Dispatcher.Invoke(TestNotice));
            menu.Items.Add("收起", null, (_, _) => Dispatcher.Invoke(Dismiss));
            menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));
            tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "灵动胶囊", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.PowerModeChanged += OnPowerChanged;
            timer.Tick += async (_, _) => await RefreshAsync(); timer.Start();
            await RefreshAsync();
            if (!System.IO.File.Exists(System.IO.Path.Combine(Settings.Folder, "settings.json")) || Environment.GetCommandLineArgs().Contains("--settings")) OpenSettings();
        };
        Closed += (_, _) => { closing = true; timer.Stop(); tray?.Dispose(); settingsWindow?.Close(); SystemEvents.DisplaySettingsChanged -= OnDisplayChanged; SystemEvents.PowerModeChanged -= OnPowerChanged; };
    }
    private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (msg == 0x02E0 || msg == 0x007E) Dispatcher.BeginInvoke(Position);
        return IntPtr.Zero;
    }
    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Position);
    private void OnPowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(() => { state.Tick(DateTimeOffset.Now); notifications.ResetBaseline(); Position(); }); }
    private void Position() { Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2; Top = SystemParameters.WorkArea.Top + 8; }
    private async Task RefreshAsync()
    {
        if (busy || closing) return;
        busy = true;
        try
        {
            state.UpdateMedia(await media.ReadAsync());
            if (settings.NotificationsEnabled) foreach (var n in await notifications.ReadAsync(settings.MutedSources)) { state.Show(n, DateTimeOffset.Now); controlsOpen = false; }
            if (closing) return;
            state.Tick(DateTimeOffset.Now); Render();
            settingsWindow?.UpdateStatus(media.Status, notifications.Status, notifications.Sources);
        }
        finally { busy = false; }
    }
    private void Render()
    {
        Clock.Text = DateTime.Now.ToString("HH:mm");
        var mode = state.Mode;
        var showMedia = mode == IslandMode.Media || (mode == IslandMode.Clock && controlsOpen && state.Media.Available);
        Clock.Visibility = mode == IslandMode.Clock && !showMedia ? Visibility.Visible : Visibility.Collapsed;
        MediaPanel.Visibility = showMedia ? Visibility.Visible : Visibility.Collapsed;
        NoticePanel.Visibility = mode == IslandMode.Notification ? Visibility.Visible : Visibility.Collapsed;
        Track.Text = state.Media.Title; Artist.Text = state.Media.Artist;
        Controls.Visibility = controlsOpen ? Visibility.Visible : Visibility.Collapsed;
        if (state.Notification is { } n)
        {
            NoticeApp.Text = n.App;
            NoticeTitle.Text = settings.PrivatePreview && !state.Expanded ? "新消息 · 点击查看" : n.Title;
            NoticeTitle.TextAlignment = state.Expanded ? TextAlignment.Left : TextAlignment.Center;
            NoticeBody.Text = n.Body;
            NoticeBodyScroll.Visibility = state.Expanded ? Visibility.Visible : Visibility.Collapsed;
        }
        var width = mode == IslandMode.Notification || showMedia ? settings.Width : 104;
        var height = mode == IslandMode.Notification ? (state.Expanded ? 190 : 64) : showMedia ? (controlsOpen ? 112 : 58) : 40;
        Animate(width, height);
    }
    private void Animate(double width, double height)
    {
        if (desiredWidth == width && desiredHeight == height) return;
        desiredWidth = width; desiredHeight = height;
        var duration = TimeSpan.FromMilliseconds(settings.MotionMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(WidthProperty, new DoubleAnimation(width, duration) { EasingFunction = ease });
        BeginAnimation(HeightProperty, new DoubleAnimation(height, duration) { EasingFunction = ease });
        var left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - width) / 2;
        BeginAnimation(LeftProperty, new DoubleAnimation(left, duration) { EasingFunction = ease });
    }
    private void OnIslandClick(object sender, MouseButtonEventArgs e)
    {
        if (state.Notification is not null) { if (state.Expanded) Dismiss(); else state.Expand(DateTimeOffset.Now); }
        else if (state.Media.Available) controlsOpen = !controlsOpen;
        else OpenSettings();
        Render();
    }
    private void OnRightClick(object sender, MouseButtonEventArgs e) => OpenSettings();
    private async void OnControl(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var button = (System.Windows.Controls.Button)sender; button.IsEnabled = false;
        try { if (!await media.ControlAsync((string)button.Tag)) MessageBox.Show("播放器未响应此操作。", "灵动胶囊"); await RefreshAsync(); }
        finally { button.IsEnabled = true; }
    }
    private void Dismiss() { state.Dismiss(); controlsOpen = false; Render(); }
    private void TestNotice() { state.Show(new("self", "灵动胶囊 · 测试", "通知交互已就绪", "这是一条本地测试通知。真实系统通知需要授权；点击展开，再次点击收起。"), DateTimeOffset.Now); Render(); }
    private void ApplyCapture() { if (!SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, settings.ShowInCapture ? 0u : 0x11u)) { /* Supported capture tools only; no global capture guarantee. */ } }
    private void OpenSettings()
    {
        if (settingsWindow is not null) { settingsWindow.Activate(); return; }
        settingsWindow = new SettingsWindow(settings, async () => { var ok = await notifications.RequestAsync(); settings.NotificationsEnabled = ok; settings.Save(); return notifications.Status; }, () => { settings.Save(); ApplyCapture(); Render(); }, TestNotice);
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
    }
}
